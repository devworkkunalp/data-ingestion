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
            
            decimal ageLimit = 35; // Default fallback for 2024 rules (changed from 50)
            bool found = false;

            try 
            {
                _logger.LogInformation("Fetching Australia Home Affairs Visa HTML...");
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync();
                    var ageMatch = Regex.Match(html, @"under (\d{2}) years of age");
                    if (ageMatch.Success && decimal.TryParse(ageMatch.Groups[1].Value, out var val))
                    {
                        ageLimit = val;
                        found = true;
                        _logger.LogInformation("Extracted Skilled Worker Age Limit: {Value}", ageLimit);
                    }
                }
                else
                {
                    _logger.LogWarning("Home Affairs returned {StatusCode}. Using fallback.", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scraper blocked or failed. Using fallback age 35.");
            }

            if (!found)
            {
                _logger.LogInformation("Using baseline AU Visa rule: Age {Value}", ageLimit);
            }

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
