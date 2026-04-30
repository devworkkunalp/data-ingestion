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
/// F-10: Australia Universities Pipeline (QILT)
/// Dynamic streaming of QILT Graduate Outcomes Survey CSV.
/// No static fallbacks.
/// </summary>
public class AustraliaUniFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<AustraliaUniFunction> _logger;

    public AustraliaUniFunction(IHttpClientFactory httpClientFactory, IngestionDbContext ingestionDb, ILogger<AustraliaUniFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("AustraliaUniSync")]
    public async Task Run([TimerTrigger("0 0 0 1 10 *")] TimerInfo timer)
    {
        _logger.LogInformation("F-10 AustraliaUniSync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient("QILT");
            var url = "https://www.qilt.edu.au/docs/default-source/gos/latest-csv"; // Conceptual live endpoint

            _logger.LogInformation("Downloading live QILT CSV...");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

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
                    var cricosCode = csv.GetField<string>("Institution ID");
                    var instName = csv.GetField<string>("Institution Name");
                    var empRateStr = csv.GetField<string>("Full-time Employment Rate");

                    if (string.IsNullOrEmpty(cricosCode) || string.IsNullOrEmpty(instName) || !decimal.TryParse(empRateStr, out var empRate))
                        continue;

                    var uni = await _ingestionDb.RawUniversities.FirstOrDefaultAsync(u => u.CountryCode == "AU" && u.ExternalId == cricosCode);
                    if (uni == null)
                    {
                        uni = new RawUniversity
                        {
                            CountryCode = "AU",
                            ExternalId = cricosCode,
                            Name = instName,
                            TuitionIntl = 0,
                            TuitionCurrency = "AUD",
                            FetchedAt = fetchedAt,
                            SourceCode = "QILT"
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
                            DataYear = DateTime.UtcNow.Year - 1,
                            DataSource = "QILT-GOS",
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
                    _logger.LogDebug(ex, "Failed to parse QILT row.");
                }
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-10 AustraliaUniSync complete. Updated {Count} AU universities.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-10 AustraliaUniSync failed");
            throw; // Fail hard in dev
        }
    }
}
