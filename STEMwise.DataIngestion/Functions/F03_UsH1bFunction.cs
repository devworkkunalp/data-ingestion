using HtmlAgilityPack;
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

            var rows = doc.DocumentNode.SelectNodes("//table//tr[td]"); 
            if (rows == null)
            {
                _logger.LogWarning("Could not find any data rows on the page.");
                return;
            }

            _logger.LogInformation("Found {Count} raw rows to parse.", rows.Count);

            int recordsAdded = 0;
            var fetchedAt = DateTime.UtcNow;

            foreach (var row in rows)
            {
                try
                {
                    var cols = row.SelectNodes("td");
                    if (cols == null || cols.Count < 5) continue;

                    var employer = HtmlEntity.DeEntitize(cols[0].InnerText ?? "").Trim();
                    var socTitle = HtmlEntity.DeEntitize(cols[1].InnerText ?? "").Trim();
                    var salaryStr = HtmlEntity.DeEntitize(cols[2].InnerText ?? "").Trim().Replace(",", "");
                    var location = HtmlEntity.DeEntitize(cols[3].InnerText ?? "").Trim();
                    var status = HtmlEntity.DeEntitize(cols[6].InnerText ?? "").Trim();

                    // Flexible status check
                    if (!status.Contains("CERTIFIED", StringComparison.OrdinalIgnoreCase)) continue;
                    
                    if (!decimal.TryParse(System.Text.RegularExpressions.Regex.Replace(salaryStr, @"[^\d\.]", ""), out var prevailingWage)) continue;

                    string city = location;
                    string state = "";
                    if (location.Contains(","))
                    {
                        var parts = location.Split(',');
                        city = parts[0].Trim();
                        state = parts[1].Trim();
                    }

                    _ingestionDb.RawH1bRecords.Add(new RawH1bRecord
                    {
                        EmployerName = employer.Length > 100 ? employer[..100] : employer,
                        SocCode = "15-1132",
                        SocTitle = socTitle.Length > 100 ? socTitle[..100] : socTitle,
                        WageLevel = 2,
                        PrevailingWage = prevailingWage,
                        CaseStatus = status,
                        Quarter = "FY2024",
                        FetchedAt = fetchedAt,
                        WorksiteCity = city,
                        WorksiteState = state
                    });

                    recordsAdded++;
                    if (recordsAdded >= 1000) break; // Cap it
                }
                catch { }
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
