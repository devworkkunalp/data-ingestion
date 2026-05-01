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
            var client = _httpClientFactory.CreateClient();
            var url = "https://www.qilt.edu.au/docs/default-source/default-document-library/gos_2024_national_report_tables.zip?sfvrsn=96058c50_1";

            _logger.LogInformation("Downloading live QILT ZIP...");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("QILT endpoint returned {StatusCode}.", response.StatusCode);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            
            _logger.LogInformation("Found {Count} files in ZIP: {Files}", archive.Entries.Count, string.Join(", ", archive.Entries.Select(e => e.Name)));

            var csvEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            
            if (csvEntry == null)
            {
                _logger.LogWarning("No CSV found in QILT ZIP.");
                return;
            }

            await using var csvStream = csvEntry.Open();
            using var reader = new StreamReader(csvStream);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, MissingFieldFound = null, BadDataFound = null };
            using var csv = new CsvReader(reader, config);

            var fetchedAt = DateTime.UtcNow;
            int updated = 0;

            while (await csv.ReadAsync())
            {
                try
                {
                    var instName = csv.GetField<string>("Institution Name") 
                                ?? csv.GetField<string>("Institution")
                                ?? csv.GetField<string>("INSTITUTION");
                    
                    var empRateStr = csv.GetField<string>("Full-time Employment Rate") 
                                  ?? csv.GetField<string>("Employment Rate")
                                  ?? csv.GetField<string>("EMPLOYMENT");

                    if (string.IsNullOrEmpty(instName) || string.IsNullOrEmpty(empRateStr))
                        continue;

                    if (!decimal.TryParse(empRateStr.Replace("%", "").Trim(), out var empRate))
                        empRate = 0;

                    var cricosCode = "AU" + Math.Abs(instName.GetHashCode()).ToString();

                    var uni = await _ingestionDb.RawUniversities.FirstOrDefaultAsync(u => u.CountryCode == "AU" && (u.ExternalId == cricosCode || u.Name == instName));
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
                            SourceCode = "QILT-GOS-2024"
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
                            DataYear = 2024,
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
                catch { }
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-10 AustraliaUniSync complete. Updated {Count} AU universities.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-10 AustraliaUniSync failed.");
        }
    }
}
