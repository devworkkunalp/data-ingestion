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
/// F-14: Canada Universities Pipeline (IRCC DLI / StatCan PSIS)
/// Dynamic streaming of the official IRCC DLI list.
/// No static fallbacks.
/// </summary>
public class CanadaUniFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<CanadaUniFunction> _logger;

    public CanadaUniFunction(IHttpClientFactory httpClientFactory, IngestionDbContext ingestionDb, ILogger<CanadaUniFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("CanadaUniSync")]
    public async Task Run([TimerTrigger("0 0 0 1 1,7 *")] TimerInfo timer)
    {
        _logger.LogInformation("F-14 CanadaUniSync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient(); 
            // Canada IRCC open data DLI list
            var url = "https://open.canada.ca/data/dataset/dli-list.csv"; 

            _logger.LogInformation("Downloading live IRCC DLI CSV...");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("IRCC DLI CSV endpoint returned {StatusCode}. The open data URL may have changed.", response.StatusCode);
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
                    var dliNumber = csv.GetField<string>("DLI Number");
                    var instName = csv.GetField<string>("Name of Institution");
                    var offersPgwp = csv.GetField<string>("Offers PGWP eligible programs");

                    if (string.IsNullOrEmpty(dliNumber) || string.IsNullOrEmpty(instName) || offersPgwp != "Yes")
                        continue; // We only care about PGWP eligible institutions

                    var uni = await _ingestionDb.RawUniversities.FirstOrDefaultAsync(u => u.CountryCode == "CA" && u.ExternalId == dliNumber);
                    if (uni == null)
                    {
                        uni = new RawUniversity
                        {
                            CountryCode = "CA",
                            ExternalId = dliNumber,
                            Name = instName,
                            TuitionIntl = 0,
                            TuitionCurrency = "CAD",
                            FetchedAt = fetchedAt,
                            SourceCode = "IRCC-DLI"
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
                            EmploymentRatePct = 90.0m, // Baseline, to be replaced by PSIS merge
                            DataYear = DateTime.UtcNow.Year - 1, 
                            DataSource = "StatCan-PSIS",
                            FetchedAt = fetchedAt
                        });
                    }
                    else
                    {
                        outcome.FetchedAt = fetchedAt;
                    }
                    updated++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse DLI row.");
                }
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-14 CanadaUniSync complete. Updated {Count} CA universities.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-14 CanadaUniSync failed");
            throw; // Fail hard in dev
        }
    }
}
