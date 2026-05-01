using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;
using ClosedXML.Excel;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-10: Australia Universities Pipeline (QILT)
/// Dynamic streaming of QILT Graduate Outcomes Survey (Supports CSV and Excel).
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
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            
            var excelEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
            var csvEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

            if (excelEntry != null)
            {
                await ProcessExcelAsync(excelEntry);
            }
            else if (csvEntry != null)
            {
                await ProcessCsvAsync(csvEntry);
            }
            else
            {
                _logger.LogWarning("No data file (.xlsx or .csv) found in QILT ZIP.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-10 AustraliaUniSync failed.");
        }
    }

    private async Task ProcessExcelAsync(ZipArchiveEntry entry)
    {
        _logger.LogInformation("Parsing QILT Excel: {Name}", entry.Name);
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        using var workbook = new XLWorkbook(ms);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null) return;

        var fetchedAt = DateTime.UtcNow;
        int updated = 0;

        // Iterate through rows, skip headers (QILT reports often have many)
        foreach (var row in worksheet.RowsUsed().Skip(5)) 
        {
            try 
            {
                var instName = row.Cell(1).GetString(); // Adjust column based on observation
                var empRateStr = row.Cell(2).GetString(); 

                if (string.IsNullOrEmpty(instName) || instName.Length < 3) continue;

                var empRate = decimal.TryParse(empRateStr.Replace("%", "").Trim(), out var val) ? val : 0;
                await UpsertUniversityAsync(instName, empRate, fetchedAt);
                updated++;
            }
            catch { }
        }
        _logger.LogInformation("F-10 AustraliaUniSync complete. Updated {Count} universities from Excel.", updated);
    }

    private async Task ProcessCsvAsync(ZipArchiveEntry entry)
    {
        _logger.LogInformation("Parsing QILT CSV: {Name}", entry.Name);
        await using var csvStream = entry.Open();
        using var reader = new StreamReader(csvStream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, MissingFieldFound = null };
        using var csv = new CsvReader(reader, config);

        var fetchedAt = DateTime.UtcNow;
        int updated = 0;
        while (await csv.ReadAsync())
        {
            try {
                var instName = csv.GetField<string>(0);
                var empRateStr = csv.GetField<string>(1);
                if (string.IsNullOrEmpty(instName)) continue;
                var empRate = decimal.TryParse(empRateStr?.Replace("%", "").Trim(), out var val) ? val : 0;
                await UpsertUniversityAsync(instName, empRate, fetchedAt);
                updated++;
            } catch { }
        }
        _logger.LogInformation("F-10 AustraliaUniSync complete. Updated {Count} universities from CSV.", updated);
    }

    private async Task UpsertUniversityAsync(string name, decimal empRate, DateTime fetchedAt)
    {
        var extId = "AU" + Math.Abs(name.GetHashCode()).ToString();
        var uni = await _ingestionDb.RawUniversities.FirstOrDefaultAsync(u => u.CountryCode == "AU" && (u.ExternalId == extId || u.Name == name));
        
        if (uni == null)
        {
            uni = new RawUniversity {
                CountryCode = "AU", ExternalId = extId, Name = name,
                TuitionIntl = 0, TuitionCurrency = "AUD", FetchedAt = fetchedAt, SourceCode = "QILT-LIVE"
            };
            _ingestionDb.RawUniversities.Add(uni);
            await _ingestionDb.SaveChangesAsync();
        }

        var outcome = await _ingestionDb.RawUniversityOutcomes.FirstOrDefaultAsync(o => o.RawUniversityId == uni.Id && o.RoleSlug == "software-engineer");
        if (outcome == null)
        {
            _ingestionDb.RawUniversityOutcomes.Add(new RawUniversityOutcome {
                RawUniversityId = uni.Id, RoleSlug = "software-engineer",
                EmploymentRatePct = empRate, DataYear = 2024, DataSource = "QILT-GOS", FetchedAt = fetchedAt
            });
        }
        else
        {
            outcome.EmploymentRatePct = empRate;
            outcome.FetchedAt = fetchedAt;
        }
        await _ingestionDb.SaveChangesAsync();
    }
}
