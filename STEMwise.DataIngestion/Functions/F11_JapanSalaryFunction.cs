using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;
using System.Text.Json;

namespace STEMwise.DataIngestion.Functions;

public class JapanSalaryFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<JapanSalaryFunction> _logger;

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
    public async Task Run([TimerTrigger("0 0 0 1 5 *")] TimerInfo timer)
    {
        _logger.LogInformation("F-11 JapanSalarySync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            
            var url = "https://nenshu-shushi.jp/nenshu/shokushu/se";
            long medianAnnual = 5500000; // Baseline JPY

            try
            {
                _logger.LogInformation("Scraping Japan Software Engineer Salary...");
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.LoadHtml(html);

                    var salaryNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'nenshu-value')]")
                                  ?? doc.DocumentNode.SelectSingleNode("//span[contains(text(), '万円')]")
                                  ?? doc.DocumentNode.SelectSingleNode("//th[contains(text(), '平均年収')]/following-sibling::td");
                    
                    if (salaryNode != null)
                    {
                        var text = HtmlAgilityPack.HtmlEntity.DeEntitize(salaryNode.InnerText);
                        var match = System.Text.RegularExpressions.Regex.Match(text.Replace(",", ""), @"(\d+(\.\d+)?)");
                        if (match.Success && double.TryParse(match.Groups[1].Value, out var nenshu))
                        {
                            if (nenshu < 10000) medianAnnual = (long)(nenshu * 10000);
                            else medianAnnual = (long)nenshu;
                            _logger.LogInformation("Successfully scraped salary: {Value} JPY", medianAnnual);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Japan site blocked (Status: {Status}). Using baseline.", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Japan scraper failed. Proceeding with baseline 5.5M JPY.");
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
                    OccupationCode = "MHLW-JP",
                    CurrencyCode = "JPY",
                    Median = medianAnnual,
                    Pct25 = pct25,
                    Pct75 = pct75,
                    DataCollectionYear = dataYear,
                    FetchedAt = fetchedAt,
                    SourceCode = "JP-SALARY-MIRROR"
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
            _logger.LogInformation("F-11 JapanSalarySync complete. Median salary: {Median} JPY.", medianAnnual);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-11 JapanSalarySync failed due to an exception.");
        }
    }
}
