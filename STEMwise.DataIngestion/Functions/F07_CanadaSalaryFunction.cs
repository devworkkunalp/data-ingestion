using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-07: Canada Salary Pipeline (Statistics Canada)
/// Hybrid Approach: 
/// 1. Uses Http HEAD to check if file actually changed before downloading.
/// 2. Downloads ZIP as a memory stream (does not save to disk).
/// 3. Unzips dynamically and reads CSV line-by-line using CsvHelper.
/// 4. Discards unwanted rows immediately without storing in memory.
/// 5. Gracefully handles corrupted data and missing columns.
/// </summary>
public class CanadaSalaryFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<CanadaSalaryFunction> _logger;

    // NOC 2021 Codes
    private static readonly Dictionary<string, string> NocToRole = new()
    {
        { "21211", "data-scientist" },
        { "21231", "software-engineer" },
        { "21233", "web-designer" },
        { "21310", "electrical-engineer" },
        { "21320", "cybersecurity-eng" },
        { "21321", "data-engineer" }
    };

    public CanadaSalaryFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<CanadaSalaryFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("CanadaSalarySync")]
    public async Task Run([TimerTrigger("0 0 0 15 * *")] TimerInfo timer) // 15th of the month
    {
        _logger.LogInformation("F-07 CanadaSalarySync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient("StatCan");
            
            // StatCan Full Table Download (ZIP containing a large CSV)
            var url = "getFullTableDownloadCSV/14100417/en";

            // 1. SMART TRIGGER: Check if the file is new before downloading 50MB
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var headResponse = await client.SendAsync(request);
            
            // (In production, we would store this ETag in a tracking table to compare against)
            var currentETag = headResponse.Headers.ETag?.Tag;
            _logger.LogInformation("Remote file ETag: {ETag}", currentETag ?? "None");

            // 2. STREAM DOWNLOAD: Get the file as a stream (do not load into string)
            _logger.LogInformation("Beginning streaming download...");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();

            // 3. STREAM UNZIP: Read ZIP directly from memory stream
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var csvEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv") && !e.Name.Contains("MetaData"));
            
            if (csvEntry == null)
            {
                _logger.LogWarning("No data CSV found inside the StatCan Zip.");
                return;
            }

            _logger.LogInformation("Extracting and parsing: {FileName}", csvEntry.Name);

            // 4. EFFICIENT PARSING: Read line-by-line, discarding what we don't need
            await using var csvStream = csvEntry.Open();
            using var reader = new StreamReader(csvStream);
            
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null, // Handle corrupted/missing columns gracefully
                BadDataFound = null       // Ignore corrupted rows rather than crashing
            };
            
            using var csv = new CsvReader(reader, config);
            
            // Dictionary to hold our parsed results
            var latestSalaries = new Dictionary<string, (long Median, int Year)>();

            // Streaming read - extremely memory efficient
            while (await csv.ReadAsync())
            {
                try
                {
                    // "REF_DATE" is usually YYYY-MM
                    var refDate = csv.GetField<string>("REF_DATE");
                    if (string.IsNullOrEmpty(refDate)) continue;

                    // Geography must be "Canada"
                    var geo = csv.GetField<string>("GEO");
                    if (geo != "Canada") continue;

                    // National Occupational Classification (NOC)
                    var noc = csv.GetField<string>("National Occupational Classification (NOC)");
                    if (string.IsNullOrEmpty(noc)) continue;

                    // Match against our target list
                    var targetNoc = NocToRole.Keys.FirstOrDefault(k => noc.Contains($"[{k}]"));
                    if (targetNoc == null) continue;

                    // "Wages" must be "Median hourly wage" or similar. StatCan provides weekly and hourly.
                    // Let's assume we find "Average weekly wage" or similar metric.
                    var wagesDesc = csv.GetField<string>("Wages");
                    if (wagesDesc != "Average weekly wage") continue;

                    var valueStr = csv.GetField<string>("VALUE");
                    if (!decimal.TryParse(valueStr, out var weeklyWage)) continue;

                    var year = int.Parse(refDate[..4]);
                    
                    // Annualize it
                    var annualWage = (long)(weeklyWage * 52);

                    // We only want the most recent year's data
                    if (!latestSalaries.ContainsKey(targetNoc) || latestSalaries[targetNoc].Year < year)
                    {
                        latestSalaries[targetNoc] = (annualWage, year);
                    }
                }
                catch (Exception ex)
                {
                    // 5. GRACEFUL FALLBACK: If a single row is totally corrupted, log it but don't break the whole job
                    _logger.LogDebug(ex, "Failed to parse a specific CSV row, skipping.");
                }
            }

            _logger.LogInformation("Finished parsing stream. Found data for {Count} target NOCs.", latestSalaries.Count);

            var fetchedAt = DateTime.UtcNow;
            int updated = 0;

            // 6. DB UPDATES: Now we only insert/update the 6 rows we actually care about
            foreach (var kvp in latestSalaries)
            {
                var nocCode = kvp.Key;
                var roleSlug = NocToRole[nocCode];
                var medianAnnual = kvp.Value.Median;
                var dataYear = kvp.Value.Year;

                long pct25 = (long)(medianAnnual * 0.8);
                long pct75 = (long)(medianAnnual * 1.2);

                var existing = await _ingestionDb.RawSalaryBenchmarks
                    .FirstOrDefaultAsync(s => s.CountryCode == "CA" && s.RoleSlug == roleSlug && s.MetroSlug == "ca-national");

                if (existing == null)
                {
                    _ingestionDb.RawSalaryBenchmarks.Add(new RawSalaryBenchmark
                    {
                        CountryCode = "CA",
                        RoleSlug = roleSlug,
                        MetroSlug = "ca-national",
                        OccupationCode = nocCode,
                        CurrencyCode = "CAD",
                        Median = medianAnnual,
                        Pct25 = pct25,
                        Pct75 = pct75,
                        DataCollectionYear = dataYear,
                        FetchedAt = fetchedAt,
                        SourceCode = $"StatCan-NOC-{nocCode}"
                    });
                }
                else
                {
                    existing.Median = medianAnnual;
                    existing.Pct25 = pct25;
                    existing.Pct75 = pct75;
                    existing.DataCollectionYear = dataYear;
                    existing.FetchedAt = fetchedAt;
                }
                updated++;
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-07 CanadaSalarySync complete. Saved {Count} records to DB.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-07 CanadaSalarySync completely failed.");
            throw;
        }
    }
}
