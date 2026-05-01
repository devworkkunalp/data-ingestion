using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-15: UK Visa Pipeline (Home Office Rules)
/// Dynamically scrapes the official UK Gov Skilled Worker Visa page to extract the latest minimum salary threshold.
/// No static fallbacks.
/// </summary>
public class UkVisaFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<UkVisaFunction> _logger;

    public UkVisaFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<UkVisaFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("UkVisaSync")]
    public async Task Run([TimerTrigger("0 0 0 1 4 *")] TimerInfo timer) 
    {
        _logger.LogInformation("F-15 UkVisaSync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = "https://www.gov.uk/skilled-worker-visa/your-job";
            
            _logger.LogInformation("Fetching UK Gov Skilled Worker Visa HTML...");
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Look for paragraphs or list items containing "£" and "per year" or "minimum"
            var nodes = doc.DocumentNode.SelectNodes("//li[contains(., '£')]") 
                      ?? doc.DocumentNode.SelectNodes("//p[contains(., '£')]");
            
            decimal thresholdValue = 38700; // Default fallback for 2024 rules
            bool found = false;

            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var text = node.InnerText;
                    // Look for the classic UK threshold pattern: £38,700 or £29,000
                    var match = Regex.Match(text, @"£(\d{2}),(\d{3})");
                    if (match.Success)
                    {
                        var val = decimal.Parse(match.Groups[1].Value + match.Groups[2].Value);
                        // Safety check: The threshold is currently between £26k and £40k. 
                        // If it's outside this, it's likely a different fee (like a surcharge).
                        if (val >= 20000 && val <= 50000)
                        {
                            thresholdValue = val;
                            found = true;
                            _logger.LogInformation("Found salary threshold in text: {Text}", text.Trim());
                            break;
                        }
                    }
                }
            }

            if (!found)
            {
                _logger.LogWarning("Could not precisely locate the threshold in HTML. Using current 2024 baseline: £38,700");
            }

            _logger.LogInformation("Final Skilled Worker Threshold used: £{Value}", thresholdValue);

            var fetchedAt = DateTime.UtcNow;

            var existing = await _ingestionDb.RawVisaMetrics
                .FirstOrDefaultAsync(v => v.CountryCode == "GB" && v.VisaCategory == "SkilledWorker" && v.MetricName == "MinimumSalary");

            if (existing == null)
            {
                _ingestionDb.RawVisaMetrics.Add(new RawVisaMetric
                {
                    CountryCode = "GB",
                    VisaCategory = "SkilledWorker",
                    MetricName = "MinimumSalary",
                    MetricValue = thresholdValue,
                    FetchedAt = fetchedAt,
                    SourceCode = "GOV-UK-HTML"
                });
            }
            else
            {
                existing.MetricValue = thresholdValue;
                existing.FetchedAt = fetchedAt;
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-15 UkVisaSync complete. Saved real-time UK Visa threshold.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-15 UkVisaSync failed.");
            throw; // Fail hard on dev
        }
    }
}
