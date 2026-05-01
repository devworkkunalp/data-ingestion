using ClosedXML.Excel;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-03: USA H-1B / LCA Pipeline (Web Scraper)
/// Scrapes H1B prevailing wage data directly from public aggregator tables (h1bdata.info)
/// bypassing the DOL Cloudflare firewall and massive Excel downloads.
/// </summary>
public class UsH1bFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<UsH1bFunction> _logger;

    public UsH1bFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<UsH1bFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("UsH1bSync")]
    public async Task Run([TimerTrigger("0 0 0 15 1,4,7,10 *")] TimerInfo timer) 
    {
        _logger.LogInformation("F-03 UsH1bSync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            
            // Public aggregator for H1B LCA data (Software Engineer, 2024)
            var url = "https://h1bdata.info/index.php?em=&job=Software+Engineer&city=&year=2024";

            _logger.LogInformation("Scraping live H-1B HTML table...");
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("H1B aggregator endpoint returned {StatusCode}. The URL might be blocked or changed.", response.StatusCode);
                return;
            }

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Find the main data table
            var table = doc.DocumentNode.SelectSingleNode("//table[@id='myTable']");
            if (table == null)
            {
                _logger.LogWarning("Could not find the H1B data table on the page.");
                return;
            }

            var rows = table.SelectNodes(".//tbody/tr");
            if (rows == null)
            {
                _logger.LogWarning("H1B data table was empty.");
                return;
            }

            int recordsAdded = 0;
            var fetchedAt = DateTime.UtcNow;

            _logger.LogInformation("Parsing live H-1B rows from HTML table...");

            // Take the first 500 rows for the pipeline
            foreach (var row in rows.Take(500))
            {
                try
                {
                    var cols = row.SelectNodes("td");
                    if (cols == null || cols.Count < 7) continue;

                    var employer = cols[0].InnerText.Trim();
                    var socTitle = cols[1].InnerText.Trim(); // Job Title
                    var salaryStr = cols[2].InnerText.Trim().Replace(",", "");
                    var city = cols[3].InnerText.Trim();
                    var state = cols[4].InnerText.Trim();
                    var submitDate = cols[5].InnerText.Trim();
                    var status = cols[6].InnerText.Trim();

                    if (status != "CERTIFIED") continue;
                    if (!decimal.TryParse(salaryStr, out var prevailingWage)) continue;

                    _ingestionDb.RawH1bRecords.Add(new RawH1bRecord
                    {
                        EmployerName = employer.Length > 100 ? employer[..100] : employer,
                        SocCode = "15-1132", // Default SWE SOC Code
                        SocTitle = socTitle.Length > 100 ? socTitle[..100] : socTitle,
                        WageLevel = 2, // Assume level II for averages
                        PrevailingWage = prevailingWage,
                        CaseStatus = status,
                        Quarter = "FY2024",
                        FetchedAt = fetchedAt,
                        WorksiteCity = city,
                        WorksiteState = state
                    });

                    recordsAdded++;
                }
                catch
                {
                    // Skip corrupt rows
                }
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-03 UsH1bSync complete. Parsed and saved {Count} live records.", recordsAdded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-03 UsH1bSync failed due to an exception. Check DB connection strings or network.");
            // Do not throw to prevent crashing the entire Function App runtime
        }
    }
}
