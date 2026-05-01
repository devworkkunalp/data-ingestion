using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-08: Canada Visa & PGWP Pipeline (Web Scraper)
/// Scrapes the live Express Entry (EE) Rounds of Invitations HTML page
/// to capture the latest CRS cutoffs, bypassing the timeout-prone JSON feed.
/// </summary>
public class CanadaVisaFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<CanadaVisaFunction> _logger;

    public CanadaVisaFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<CanadaVisaFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("CanadaVisaSync")]
    public async Task Run([TimerTrigger("0 0 0 15 1,4,7,10 *")] TimerInfo timer) // Quarterly
    {
        _logger.LogInformation("F-08 CanadaVisaSync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            
            var url = "https://www.canada.ca/en/immigration-refugees-citizenship/corporate/mandate/policies-operational-instructions-agreements/ministerial-instructions/express-entry-rounds.html";

            _logger.LogInformation("Scraping live Express Entry rounds HTML...");
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Express Entry HTML endpoint returned {StatusCode}.", response.StatusCode);
                return;
            }

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var table = doc.DocumentNode.SelectSingleNode("//table");
            if (table == null)
            {
                _logger.LogWarning("Could not find the EE rounds table.");
                return;
            }

            int latestGeneralCrs = 0;
            int latestStemCrs = 0;
            var fetchedAt = DateTime.UtcNow;

            var rows = table.SelectNodes(".//tr");
            if (rows != null)
            {
                foreach (var row in rows.Skip(1))
                {
                    var cols = row.SelectNodes("td|th");
                    if (cols == null || cols.Count < 6) continue;

                    var drawType = HtmlEntity.DeEntitize(cols[2].InnerText ?? "").Trim();
                    var crsStr = HtmlEntity.DeEntitize(cols[5].InnerText ?? "").Trim();

                    var numericPart = System.Text.RegularExpressions.Regex.Replace(crsStr, @"[^\d]", "");
                    if (string.IsNullOrEmpty(numericPart) || !int.TryParse(numericPart, out int crsScore))
                        continue;

                    if (latestGeneralCrs == 0 && (drawType.Contains("General", StringComparison.OrdinalIgnoreCase) || drawType.Contains("No program specified", StringComparison.OrdinalIgnoreCase)))
                    {
                        latestGeneralCrs = crsScore;
                    }

                    if (latestStemCrs == 0 && drawType.Contains("STEM", StringComparison.OrdinalIgnoreCase))
                    {
                        latestStemCrs = crsScore;
                    }

                    if (latestGeneralCrs > 0 && latestStemCrs > 0)
                        break;
                }
            }

            _logger.LogInformation("Extracted CRS Scores - General: {General}, STEM: {Stem}", latestGeneralCrs, latestStemCrs);

            if (latestGeneralCrs > 0) await SaveMetricAsync("ExpressEntry", "GeneralCRSCutoff", latestGeneralCrs, fetchedAt);
            if (latestStemCrs > 0) await SaveMetricAsync("ExpressEntry", "StemCRSCutoff", latestStemCrs, fetchedAt);

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-08 CanadaVisaSync complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-08 CanadaVisaSync failed due to an exception.");
        }
    }

    private async Task SaveMetricAsync(string category, string name, decimal value, DateTime fetchedAt)
    {
        var existing = await _ingestionDb.RawVisaMetrics
            .FirstOrDefaultAsync(v => v.CountryCode == "CA" && v.VisaCategory == category && v.MetricName == name);

        if (existing == null)
        {
            _ingestionDb.RawVisaMetrics.Add(new RawVisaMetric
            {
                CountryCode = "CA",
                VisaCategory = category,
                MetricName = name,
                MetricValue = value,
                FetchedAt = fetchedAt,
                SourceCode = "IRCC-EE-JSON"
            });
        }
        else
        {
            existing.MetricValue = value;
            existing.FetchedAt = fetchedAt;
        }
    }
}
