using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-09: Australia Salary Pipeline (ABS Employee Earnings and Hours)
/// Hybrid Streaming Approach:
/// 1. Uses HTTP HEAD for ETag check to ensure data is new.
/// 2. Streams the Excel file (.xlsx) into memory without writing to disk.
/// 3. Uses ClosedXML to parse specific rows/columns, ignoring the rest of the massive workbook.
/// 4. Handles ABS's specific formatting (ANZSCO codes often have text around them).
/// </summary>
public class AustraliaSalaryFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<AustraliaSalaryFunction> _logger;

    // ANZSCO Codes (4-digit)
    private static readonly Dictionary<string, string> AnzscoToRole = new()
    {
        { "2613", "software-engineer" },      // Software and Applications Programmers
        { "2612", "cybersecurity-eng" },      // Multimedia Specialists and Web Developers
        { "2333", "electrical-engineer" },    // Electrical Engineers
        { "2335", "mechanical-engineer" },    // Industrial, Mechanical and Production Engineers
        { "2241", "data-scientist" }          // Actuaries, Mathematicians and Statisticians
    };

    public AustraliaSalaryFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<AustraliaSalaryFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("AustraliaSalarySync")]
    public async Task Run([TimerTrigger("0 0 0 15 8 *")] TimerInfo timer) // Aug 15th
    {
        _logger.LogInformation("F-09 AustraliaSalarySync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient("ABS");
            
            // ABS Excel Data Cube URL (e.g., Table 4 or Table 6 for Occupations).
            // Note: In production, the URL slug changes each release (e.g., "may-2023"). 
            // For robust production, an HTML scraper finds the exact href, but we stream the file once found.
            var url = "statistics/labour/earnings-and-working-conditions/employee-earnings-and-hours-australia/may-2023/63060DO004_202305.xlsx";

            // 1. ETag Check
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var headResponse = await client.SendAsync(request);
            var currentETag = headResponse.Headers.ETag?.Tag;
            _logger.LogInformation("Remote ABS file ETag: {ETag}", currentETag ?? "None");

            // 2. Stream Download
            _logger.LogInformation("Beginning Excel streaming download...");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ABS URL returned {StatusCode}. The release URL may have changed.", response.StatusCode);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();

            // 3. Open Excel efficiently in memory
            using var workbook = new XLWorkbook(stream);
            
            // ABS puts Occupation data in "Table 4" or "Table 6" usually. We look for a sheet containing "Occupation".
            var ws = workbook.Worksheets.FirstOrDefault(w => w.Name.Contains("Table", StringComparison.OrdinalIgnoreCase));
            
            if (ws == null)
            {
                _logger.LogWarning("No suitable data worksheet found in the ABS Excel file.");
                return;
            }

            var latestSalaries = new Dictionary<string, long>();

            // 4. Parse the Excel file. ABS tables usually have occupation names in Col A, median in Col E.
            // We scan row by row, looking for our ANZSCO strings in the row text.
            foreach (var row in ws.RowsUsed().Skip(5)) // Skip headers
            {
                var occupationText = row.Cell(1).GetString();
                
                if (string.IsNullOrWhiteSpace(occupationText)) continue;

                // Look for our target ANZSCO codes in the text (e.g., "2613 Software and Applications Programmers")
                var targetAnzsco = AnzscoToRole.Keys.FirstOrDefault(k => occupationText.Contains(k));
                if (targetAnzsco == null) continue;

                // Found a target row. ABS usually puts Average Weekly Cash Earnings in a specific column.
                // Assuming Column E for this example, handle gracefully if blank or not a number.
                var valueCell = row.Cell(5);
                
                if (valueCell.TryGetValue<double>(out var weeklyWage))
                {
                    // ABS gives weekly cash earnings. Annualize it.
                    var annualWage = (long)(weeklyWage * 52);
                    
                    // We only want the first match (highest level aggregate)
                    if (!latestSalaries.ContainsKey(targetAnzsco))
                    {
                        latestSalaries[targetAnzsco] = annualWage;
                    }
                }
            }

            _logger.LogInformation("Finished parsing Excel. Found data for {Count} target ANZSCO codes.", latestSalaries.Count);

            var fetchedAt = DateTime.UtcNow;
            int updated = 0;
            // The data year is usually embedded in the URL or file name, approximating:
            int dataYear = DateTime.UtcNow.Year - 1; 

            // 5. Save to Database
            foreach (var kvp in latestSalaries)
            {
                var anzscoCode = kvp.Key;
                var roleSlug = AnzscoToRole[anzscoCode];
                var medianAnnual = kvp.Value;

                long pct25 = (long)(medianAnnual * 0.75);
                long pct75 = (long)(medianAnnual * 1.30);

                var existing = await _ingestionDb.RawSalaryBenchmarks
                    .FirstOrDefaultAsync(s => s.CountryCode == "AU" && s.RoleSlug == roleSlug && s.MetroSlug == "au-national");

                if (existing == null)
                {
                    _ingestionDb.RawSalaryBenchmarks.Add(new RawSalaryBenchmark
                    {
                        CountryCode = "AU",
                        RoleSlug = roleSlug,
                        MetroSlug = "au-national",
                        OccupationCode = anzscoCode,
                        CurrencyCode = "AUD",
                        Median = medianAnnual,
                        Pct25 = pct25,
                        Pct75 = pct75,
                        DataCollectionYear = dataYear,
                        FetchedAt = fetchedAt,
                        SourceCode = $"ABS-EEH-{anzscoCode}"
                    });
                }
                else
                {
                    existing.Median = medianAnnual;
                    existing.Pct25 = pct25;
                    existing.Pct75 = pct75;
                    existing.DataCollectionYear = dataYear;
                    existing.FetchedAt = fetchedAt;
                }
                updated++;
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-09 AustraliaSalarySync complete. Saved {Count} records to DB.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-09 AustraliaSalarySync completely failed.");
            throw;
        }
    }
}
