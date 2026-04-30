using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-16: Australia Visa Pipeline (Home Affairs)
/// Dynamically scrapes the official Home Affairs Temporary Graduate visa page to extract age limits and durations.
/// No static fallbacks.
/// </summary>
public class AustraliaVisaFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<AustraliaVisaFunction> _logger;

    public AustraliaVisaFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<AustraliaVisaFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("AustraliaVisaSync")]
    public async Task Run([TimerTrigger("0 0 0 1 7 *")] TimerInfo timer) 
    {
        _logger.LogInformation("F-16 AustraliaVisaSync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = "https://immi.homeaffairs.gov.au/visas/getting-a-visa/visa-listing/temporary-graduate-485/post-higher-education-work";
            
            _logger.LogInformation("Fetching Australia Home Affairs Visa HTML...");
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync();

            // Regex to find "under X years of age" or similar age limit mention
            // Recently changed from 50 to 35
            var ageRegex = new Regex(@"under (\d{2}) years of age");
            var ageMatch = ageRegex.Match(html);

            if (!ageMatch.Success)
            {
                throw new InvalidDataException("Could not locate the age limit pattern in the Home Affairs HTML.");
            }

            if (!decimal.TryParse(ageMatch.Groups[1].Value, out var ageLimit))
            {
                throw new InvalidDataException($"Found age string but could not parse to decimal.");
            }

            _logger.LogInformation("Extracted Subclass 485 Age Limit: {Value}", ageLimit);

            var fetchedAt = DateTime.UtcNow;

            var existing = await _ingestionDb.RawVisaMetrics
                .FirstOrDefaultAsync(v => v.CountryCode == "AU" && v.VisaCategory == "Subclass485" && v.MetricName == "MaxAge");

            if (existing == null)
            {
                _ingestionDb.RawVisaMetrics.Add(new RawVisaMetric
                {
                    CountryCode = "AU",
                    VisaCategory = "Subclass485",
                    MetricName = "MaxAge",
                    MetricValue = ageLimit,
                    FetchedAt = fetchedAt,
                    SourceCode = "HOME-AFFAIRS-HTML"
                });
            }
            else
            {
                existing.MetricValue = ageLimit;
                existing.FetchedAt = fetchedAt;
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-16 AustraliaVisaSync complete. Saved real-time AU Visa threshold.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-16 AustraliaVisaSync failed.");
            throw; // Fail hard on dev
        }
    }
}
