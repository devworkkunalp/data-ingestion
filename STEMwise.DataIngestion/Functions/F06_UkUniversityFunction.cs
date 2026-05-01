using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-06: UK Universities Pipeline (HESA GOS)
/// Streams the HESA Graduate Outcomes CSV to calculate per-university employment rates.
/// No static fallbacks.
/// </summary>
public class UkUniversityFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<UkUniversityFunction> _logger;

    public UkUniversityFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<UkUniversityFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("UkUniversitySync")]
    public async Task Run([TimerTrigger("0 0 0 15 8 *")] TimerInfo timer) 
    {
        _logger.LogInformation("F-06 UkUniversitySync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient("ONS"); // Using general HTTP client
            var url = "https://www.hesa.ac.uk/data-and-analysis/graduates/releases/latest/csv"; // Expected live endpoint structure

            _logger.LogInformation("Downloading live HESA CSV...");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HESA CSV endpoint returned {StatusCode}. The URL might be blocked or changed.", response.StatusCode);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, MissingFieldFound = null, BadDataFound = null };
            using var csv = new CsvReader(reader, config);

            var fetchedAt = DateTime.UtcNow;
            int updated = 0;

            while (await csv.ReadAsync())
            {
                try
                {
                    var ukprn = csv.GetField<string>("UKPRN"); // UK Provider Reference Number
                    var instName = csv.GetField<string>("Provider Name");
                    var empRateStr = csv.GetField<string>("Employment Rate");

                    if (string.IsNullOrEmpty(ukprn) || string.IsNullOrEmpty(instName) || !decimal.TryParse(empRateStr, out var empRate))
                        continue;

                    var uni = await _ingestionDb.RawUniversities.FirstOrDefaultAsync(u => u.CountryCode == "GB" && u.ExternalId == ukprn);
                    if (uni == null)
                    {
                        uni = new RawUniversity
                        {
                            CountryCode = "GB",
                            ExternalId = ukprn,
                            Name = instName,
                            TuitionIntl = 0, // Placeholder until Unistats API integration
                            TuitionCurrency = "GBP",
                            FetchedAt = fetchedAt,
                            SourceCode = "HESA"
                        };
                        _ingestionDb.RawUniversities.Add(uni);
                    }
                    else
                    {
                        uni.FetchedAt = fetchedAt;
                    }

                    await _ingestionDb.SaveChangesAsync();

                    var outcome = await _ingestionDb.RawUniversityOutcomes.FirstOrDefaultAsync(o => o.RawUniversityId == uni.Id && o.RoleSlug == "software-engineer");
                    if (outcome == null)
                    {
                        _ingestionDb.RawUniversityOutcomes.Add(new RawUniversityOutcome
                        {
                            RawUniversityId = uni.Id,
                            RoleSlug = "software-engineer",
                            EmploymentRatePct = empRate,
                            DataYear = DateTime.UtcNow.Year - 2, 
                            DataSource = "HESA-GOS",
                            FetchedAt = fetchedAt
                        });
                    }
                    else
                    {
                        outcome.EmploymentRatePct = empRate;
                        outcome.FetchedAt = fetchedAt;
                    }
                    updated++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse HESA row.");
                }
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-06 UkUniversitySync complete. Updated {Count} UK universities.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-06 UkUniversitySync failed due to an exception. Check DB connection strings or network.");
            // Do not throw to prevent crashing the entire Function App runtime
        }
    }
}
