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

            // Regex to find "£38,700" or similar general minimum salary mention
            // Looks for £ followed by 2 digits, a comma, and 3 digits.
            var regex = new Regex(@"£(\d{2}),(\d{3})");
            var match = regex.Match(html);

            if (!match.Success)
            {
                throw new InvalidDataException("Could not locate the salary threshold pattern in the UK Gov HTML.");
            }

            var thresholdStr = match.Groups[1].Value + match.Groups[2].Value;
            if (!decimal.TryParse(thresholdStr, out var thresholdValue))
            {
                throw new InvalidDataException($"Found threshold string {thresholdStr} but could not parse to decimal.");
            }

            _logger.LogInformation("Extracted Skilled Worker Threshold: £{Value}", thresholdValue);

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
