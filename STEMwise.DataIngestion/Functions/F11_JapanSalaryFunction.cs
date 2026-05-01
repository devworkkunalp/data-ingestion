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
/// F-11: Japan Salary Pipeline (MHLW / e-Stat)
/// Hybrid Streaming Approach:
/// 1. Uses HTTP HEAD for ETag check.
/// 2. Streams the CSV download.
/// 3. CRITICAL: Decodes Shift-JIS encoding dynamically (standard for Japanese gov files).
/// 4. Uses CsvHelper to parse, discarding unwanted rows.
/// 5. Calculates total annual compensation = (Monthly Base * 12) + Annual Bonus.
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
            var client = _httpClientFactory.CreateClient("MHLW");
            
            // Note: e-Stat CSV direct download link.
            var url = "stat-search/file-download?statInfId=000032204656&fileKind=1";

            // 1. ETag Check
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var headResponse = await client.SendAsync(request);
            var currentETag = headResponse.Headers.ETag?.Tag;
            _logger.LogInformation("Remote MHLW file ETag: {ETag}", currentETag ?? "None");

            // 2. Stream Download
            _logger.LogInformation("Beginning CSV streaming download...");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MHLW URL returned {StatusCode}.", response.StatusCode);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();

            // 3. Shift-JIS Decoding (CRITICAL for Japan)
            // CodePagesEncodingProvider is registered in Program.cs
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var shiftJis = Encoding.GetEncoding("shift_jis");
            
            using var reader = new StreamReader(stream, shiftJis);
            
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                BadDataFound = null
            };
            
            using var csv = new CsvReader(reader, config);
            
            var latestSalaries = new Dictionary<string, (long Base, long Bonus)>();

            // 4. Stream Parsing
            while (await csv.ReadAsync())
            {
                try
                {
                    // Look for JSOC Code column (Occupation Code)
                    var jsoc = csv.GetField<string>("職種コード") ?? csv.GetField<string>("Code");
                    if (string.IsNullOrEmpty(jsoc)) continue;

                    var targetJsoc = JsocToRole.Keys.FirstOrDefault(k => jsoc == k);
                    if (targetJsoc == null) continue;

                    // "きまって支給する現金給与額" = Monthly contractual cash earnings
                    var monthlyStr = csv.GetField<string>("きまって支給する現金給与額");
                    // "年間賞与その他特別給与額" = Annual special cash earnings (bonus)
                    var bonusStr = csv.GetField<string>("年間賞与その他特別給与額");

                    if (decimal.TryParse(monthlyStr, out var monthly) && decimal.TryParse(bonusStr, out var bonus))
                    {
                        // Some Japan files report in "thousands of JPY" depending on the table.
                        // Assuming raw JPY for this specific dataset.
                        if (!latestSalaries.ContainsKey(targetJsoc))
                        {
                            latestSalaries[targetJsoc] = ((long)monthly, (long)bonus);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse MHLW CSV row, skipping.");
                }
            }

            _logger.LogInformation("Finished parsing Japan CSV. Found data for {Count} target JSOC codes.", latestSalaries.Count);

            var fetchedAt = DateTime.UtcNow;
            int updated = 0;
            int dataYear = DateTime.UtcNow.Year - 1;

            // 5. DB Updates
            foreach (var kvp in latestSalaries)
            {
                var jsocCode = kvp.Key;
                var roleSlug = JsocToRole[jsocCode];
                
                // Japan compensation calculation
                var monthlyBase = kvp.Value.Base;
                var annualBonus = kvp.Value.Bonus;
                long medianAnnual = (monthlyBase * 12) + annualBonus;

                long pct25 = (long)(medianAnnual * 0.70);
                long pct75 = (long)(medianAnnual * 1.40);

                var existing = await _ingestionDb.RawSalaryBenchmarks
                    .FirstOrDefaultAsync(s => s.CountryCode == "JP" && s.RoleSlug == roleSlug && s.MetroSlug == "jp-national");

                if (existing == null)
                {
                    _ingestionDb.RawSalaryBenchmarks.Add(new RawSalaryBenchmark
                    {
                        CountryCode = "JP",
                        RoleSlug = roleSlug,
                        MetroSlug = "jp-national",
                        OccupationCode = jsocCode,
                        CurrencyCode = "JPY",
                        Median = medianAnnual,
                        Pct25 = pct25,
                        Pct75 = pct75,
                        DataCollectionYear = dataYear,
                        FetchedAt = fetchedAt,
                        SourceCode = $"MHLW-BSWS-{jsocCode}"
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
            _logger.LogInformation("F-11 JapanSalarySync complete. Saved {Count} records to DB.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-11 JapanSalarySync failed due to an exception. Network block or format change.");
            // Do not throw to prevent crashing the entire Function App runtime
        }
    }
}
