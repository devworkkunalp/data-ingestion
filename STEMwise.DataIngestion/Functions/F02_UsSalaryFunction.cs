using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;
using System.Text.Json;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-02: USA Salary Pipeline (DOL BLS OES)
/// Runs: Bi-monthly (1st Jan and 1st Jul — after May release is out)
/// Source: BLS Public API v2 — https://api.bls.gov/publicAPI/v2/timeseries/data/
/// Writes to: orchestratorDB.RawSalaryBenchmarks
/// Rate limits: 500 requests/day, 50 series per request
///
/// SOC codes mapped to our role slugs:
///   software-engineer    → 15-1252
///   data-scientist       → 15-2051
///   data-engineer        → 15-1243
///   cybersecurity-eng    → 15-1212
///   electrical-engineer  → 17-2071
///   ml-engineer          → 15-2051 (closest mapping)
///   biomedical-engineer  → 17-2031
///   mechanical-engineer  → 17-2141
/// </summary>
public class UsSalaryFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly IConfiguration _config;
    private readonly ILogger<UsSalaryFunction> _logger;

    // SOC code → our role slug
    private static readonly Dictionary<string, string> SocToRole = new()
    {
        { "15-1252", "software-engineer" },
        { "15-2051", "data-scientist" },
        { "15-1243", "data-engineer" },
        { "15-1212", "cybersecurity-eng" },
        { "17-2071", "electrical-engineer" },
        { "17-2031", "biomedical-engineer" },
        { "17-2141", "mechanical-engineer" },
    };

    // OES Series ID format: OES{areaCode}{industryCode}{socCode}{dataType}
    // National estimate: OESNA0000000000{SOC}03 (03 = median annual)
    // We use national median as baseline; city data added later
    private static readonly Dictionary<string, string> MetroAreaCodes = new()
    {
        { "us-national",       "0000000" },
        { "us-san-francisco",  "0000418" },
        { "us-new-york",       "0000356" },
        { "us-seattle",        "0000426" },
        { "us-austin",         "0000113" },
        { "us-chicago",        "0000176" },
        { "us-boston",         "0000148" },
        { "us-los-angeles",    "0000310" },
    };

    // BLS data type codes
    private const string MedianAnnual = "03";  // 50th percentile annual
    private const string P25Annual    = "01";  // 25th percentile annual
    private const string P75Annual    = "06";  // 75th percentile annual

    public UsSalaryFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        IConfiguration config,
        ILogger<UsSalaryFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _config = config;
        _logger = logger;
    }

    [Function("UsSalarySync")]
    public async Task Run(
        [TimerTrigger("0 0 0 1 1,7 *")] TimerInfo timer) // Jan 1 and Jul 1
    {
        _logger.LogInformation("F-02 UsSalarySync started at {Time}", DateTime.UtcNow);

        var apiKey = _config["BlsApiKey"]
            ?? throw new InvalidOperationException("BlsApiKey not configured");

        var client = _httpClientFactory.CreateClient("BLS");

        // Process national data first (metro city data is a Phase 2 enhancement)
        // BLS API v2: POST with series array, max 50 per request
        var seriesIds = BuildNationalSeriesIds();

        _logger.LogInformation("Fetching {Count} BLS series", seriesIds.Count);

        try
        {
            // BLS API allows up to 50 series per request — batch if needed
            const int batchSize = 50;
            for (int i = 0; i < seriesIds.Count; i += batchSize)
            {
                var batch = seriesIds.Skip(i).Take(batchSize).ToList();
                await FetchAndStoreBatch(client, apiKey, batch);
                // Small delay to be respectful of rate limits
                if (i + batchSize < seriesIds.Count)
                    await Task.Delay(1000);
            }

            _logger.LogInformation("F-02 UsSalarySync complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-02 UsSalarySync failed");
            throw;
        }
    }

    private List<(string SeriesId, string RoleSlug, string MetroSlug, string DataType)> BuildNationalSeriesIds()
    {
        var series = new List<(string, string, string, string)>();

        foreach (var (socCode, roleSlug) in SocToRole)
        {
            var cleanSoc = socCode.Replace("-", "");

            // National median (P50), P25, P75 for each role
            series.Add(($"OESNA0000000000{cleanSoc}03", roleSlug, "us-national", MedianAnnual));
            series.Add(($"OESNA0000000000{cleanSoc}01", roleSlug, "us-national", P25Annual));
            series.Add(($"OESNA0000000000{cleanSoc}06", roleSlug, "us-national", P75Annual));
        }

        return series;
    }

    private async Task FetchAndStoreBatch(
        HttpClient client,
        string apiKey,
        List<(string SeriesId, string RoleSlug, string MetroSlug, string DataType)> batch)
    {
        var requestBody = new
        {
            seriesid = batch.Select(b => b.SeriesId).ToArray(),
            startyear = (DateTime.UtcNow.Year - 1).ToString(),
            endyear = DateTime.UtcNow.Year.ToString(),
            registrationkey = apiKey
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("timeseries/data/", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<BlsApiResponse>(responseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (data?.Status != "REQUEST_SUCCEEDED" || data.Results?.Series == null)
        {
            _logger.LogWarning("BLS API non-success response: {Json}", responseJson[..Math.Min(500, responseJson.Length)]);
            return;
        }

        var fetchedAt = DateTime.UtcNow;

        foreach (var seriesResult in data.Results.Series)
        {
            var matchedItem = batch.FirstOrDefault(b => b.SeriesId == seriesResult.SeriesId);
            if (matchedItem == default) continue;

            // Take the most recent year's data point
            var latestDataPoint = seriesResult.Data?
                .OrderByDescending(d => d.Year)
                .ThenByDescending(d => d.Period)
                .FirstOrDefault();

            if (latestDataPoint == null || !long.TryParse(
                latestDataPoint.Value?.Replace(",", ""), out var value)) continue;

            var existing = await _ingestionDb.RawSalaryBenchmarks
                .FirstOrDefaultAsync(s =>
                    s.CountryCode == "US" &&
                    s.RoleSlug == matchedItem.RoleSlug &&
                    s.MetroSlug == matchedItem.MetroSlug);

            if (existing == null)
            {
                var benchmark = new RawSalaryBenchmark
                {
                    CountryCode = "US",
                    RoleSlug = matchedItem.RoleSlug,
                    MetroSlug = matchedItem.MetroSlug,
                    OccupationCode = seriesResult.SeriesId,
                    CurrencyCode = "USD",
                    DataCollectionYear = int.Parse(latestDataPoint.Year),
                    FetchedAt = fetchedAt,
                    SourceCode = $"BLS-OES-{seriesResult.SeriesId}"
                };

                ApplyDataType(benchmark, matchedItem.DataType, value);
                _ingestionDb.RawSalaryBenchmarks.Add(benchmark);
            }
            else
            {
                ApplyDataType(existing, matchedItem.DataType, value);
                existing.FetchedAt = fetchedAt;
                existing.DataCollectionYear = int.Parse(latestDataPoint.Year);
            }
        }

        await _ingestionDb.SaveChangesAsync();
        _logger.LogInformation("Stored {Count} BLS salary records", batch.Count);
    }

    private static void ApplyDataType(RawSalaryBenchmark benchmark, string dataType, long value)
    {
        switch (dataType)
        {
            case MedianAnnual: benchmark.Median = value; break;
            case P25Annual:    benchmark.Pct25 = value; break;
            case P75Annual:    benchmark.Pct75 = value; break;
        }
    }
}

// --- BLS API Response Models ---
internal class BlsApiResponse
{
    public string Status { get; set; } = string.Empty;
    public BlsResults? Results { get; set; }
}

internal class BlsResults
{
    public List<BlsSeries>? Series { get; set; }
}

internal class BlsSeries
{
    public string SeriesId { get; set; } = string.Empty;
    public List<BlsDataPoint>? Data { get; set; }
}

internal class BlsDataPoint
{
    public string Year { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string? Value { get; set; }
}
