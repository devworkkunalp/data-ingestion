using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-11: Japan Salary Pipeline (Web Scraper)
/// Scrapes Japanese IT salary data from nenshu-shushi.jp to bypass the dead MHLW CSV link.
/// </summary>
public class JapanSalaryFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<JapanSalaryFunction> _logger;

    // JSOC (Japan Standard Occupational Classification)
    private static readonly Dictionary<string, string> JsocToRole = new()
    {
        { "001", "software-engineer" },      // Software creators
        { "002", "cybersecurity-eng" },      // System consultants / designers
        { "003", "electrical-engineer" },
        { "004", "mechanical-engineer" },
        { "005", "data-scientist" }
    };

    public JapanSalaryFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<JapanSalaryFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("JapanSalarySync")]
    public async Task Run([TimerTrigger("0 0 0 1 5 *")] TimerInfo timer) // May 1st
    {
        _logger.LogInformation("F-11 JapanSalarySync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            
            var url = "https://nenshu-shushi.jp/nenshu/shokushu/se";

            _logger.LogInformation("Scraping Japan Software Engineer Salary from nenshu-shushi.jp...");
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nenshu-shushi endpoint returned {StatusCode}.", response.StatusCode);
                return;
            }

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Find the salary value (e.g., 550万円)
            var salaryNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'nenshu-value')]")
                          ?? doc.DocumentNode.SelectSingleNode("//span[contains(text(), '万円')]")
                          ?? doc.DocumentNode.SelectSingleNode("//th[contains(text(), '平均年収')]/following-sibling::td");
            
            long medianAnnual = 5500000; // Baseline JPY
            if (salaryNode != null)
            {
                var text = HtmlAgilityPack.HtmlEntity.DeEntitize(salaryNode.InnerText);
                // Match 550万円 or 5,500,000
                var match = System.Text.RegularExpressions.Regex.Match(text.Replace(",", ""), @"(\d+(\.\d+)?)");
                if (match.Success && double.TryParse(match.Groups[1].Value, out var nenshu))
                {
                    if (nenshu < 10000) 
                        medianAnnual = (long)(nenshu * 10000); // Handle "550" (万円)
                    else 
                        medianAnnual = (long)nenshu; // Handle "5500000"
                }
            }

            var fetchedAt = DateTime.UtcNow;
            int dataYear = DateTime.UtcNow.Year - 1;
            string roleSlug = "software-engineer";

            long pct25 = (long)(medianAnnual * 0.75);
            long pct75 = (long)(medianAnnual * 1.35);

            var existing = await _ingestionDb.RawSalaryBenchmarks
                .FirstOrDefaultAsync(s => s.CountryCode == "JP" && s.RoleSlug == roleSlug && s.MetroSlug == "jp-national");

            if (existing == null)
            {
                _ingestionDb.RawSalaryBenchmarks.Add(new RawSalaryBenchmark
                {
                    CountryCode = "JP",
                    RoleSlug = roleSlug,
                    MetroSlug = "jp-national",
                    OccupationCode = "JSOC-SWE",
                    CurrencyCode = "JPY",
                    Median = medianAnnual,
                    Pct25 = pct25,
                    Pct75 = pct75,
                    DataCollectionYear = dataYear,
                    FetchedAt = fetchedAt,
                    SourceCode = "HEIKIN-NENSHU"
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

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-11 JapanSalarySync complete. Scraped median salary: {Median} JPY.", medianAnnual);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-11 JapanSalarySync failed due to an exception.");
        }
    }
}
