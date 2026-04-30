using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-08: Canada Visa & PGWP Pipeline (IRCC)
/// Fetches the live Express Entry (EE) Rounds of Invitations JSON feed 
/// to capture the latest Comprehensive Ranking System (CRS) cutoffs,
/// specifically looking for STEM-targeted draws or General draws.
/// </summary>
public class CanadaVisaFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<CanadaVisaFunction> _logger;

    public CanadaVisaFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<CanadaVisaFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("CanadaVisaSync")]
    public async Task Run([TimerTrigger("0 0 0 15 1,4,7,10 *")] TimerInfo timer) // Quarterly
    {
        _logger.LogInformation("F-08 CanadaVisaSync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient();
            
            // IRCC official Express Entry rounds JSON feed
            var url = "https://www.canada.ca/content/dam/ircc/documents/json/ee_rounds_123_en.json";

            _logger.LogInformation("Fetching live Express Entry rounds JSON...");
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var rounds = doc.RootElement.GetProperty("rounds");
            
            int latestGeneralCrs = 0;
            int latestStemCrs = 0;
            var fetchedAt = DateTime.UtcNow;

            // Iterate backwards (or forwards depending on JSON sort) to find the latest draws
            foreach (var round in rounds.EnumerateArray())
            {
                var drawType = round.GetProperty("drawName").GetString() ?? "";
                var crsStr = round.GetProperty("crs").GetString() ?? "0";
                
                // Remove commas if any (e.g., "1,000")
                if (!int.TryParse(crsStr.Replace(",", ""), out int crsScore) || crsScore == 0)
                    continue;

                // Capture the most recent General draw
                if (latestGeneralCrs == 0 && (drawType.Contains("General") || drawType.Contains("No program specified")))
                {
                    latestGeneralCrs = crsScore;
                }

                // Capture the most recent STEM targeted draw
                if (latestStemCrs == 0 && drawType.Contains("STEM"))
                {
                    latestStemCrs = crsScore;
                }

                // If we found both, we can stop searching
                if (latestGeneralCrs > 0 && latestStemCrs > 0)
                    break;
            }

            _logger.LogInformation("Extracted CRS Scores - General: {General}, STEM: {Stem}", latestGeneralCrs, latestStemCrs);

            // Save to Database
            if (latestGeneralCrs > 0)
            {
                await SaveMetricAsync("ExpressEntry", "GeneralCRSCutoff", latestGeneralCrs, fetchedAt);
            }
            if (latestStemCrs > 0)
            {
                await SaveMetricAsync("ExpressEntry", "StemCRSCutoff", latestStemCrs, fetchedAt);
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-08 CanadaVisaSync complete. Saved real-time Express Entry cutoffs.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-08 CanadaVisaSync failed");
            throw; // Fail hard in dev
        }
    }

    private async Task SaveMetricAsync(string category, string name, decimal value, DateTime fetchedAt)
    {
        var existing = await _ingestionDb.RawVisaMetrics
            .FirstOrDefaultAsync(v => v.CountryCode == "CA" && v.VisaCategory == category && v.MetricName == name);

        if (existing == null)
        {
            _ingestionDb.RawVisaMetrics.Add(new RawVisaMetric
            {
                CountryCode = "CA",
                VisaCategory = category,
                MetricName = name,
                MetricValue = value,
                FetchedAt = fetchedAt,
                SourceCode = "IRCC-EE-JSON"
            });
        }
        else
        {
            existing.MetricValue = value;
            existing.FetchedAt = fetchedAt;
        }
    }
}
