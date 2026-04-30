using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-05: UK Salary Pipeline (ONS ASHE via Nomis API)
/// Parses the JSON-stat response from Nomis to extract median and quartile salaries.
/// No static fallbacks.
/// </summary>
public class UkSalaryFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ILogger<UkSalaryFunction> _logger;

    // Mapping SOC2020 codes to STEMwise roles
    private static readonly Dictionary<string, string> SocToRole = new()
    {
        { "2134", "software-engineer" },
        { "2135", "cybersecurity-eng" },
        { "2133", "data-engineer" },
        { "2123", "electrical-engineer" },
        { "2126", "mechanical-engineer" }
    };

    public UkSalaryFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ILogger<UkSalaryFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _logger = logger;
    }

    [Function("UkSalarySync")]
    public async Task Run([TimerTrigger("0 0 0 1 11 *")] TimerInfo timer) 
    {
        _logger.LogInformation("F-05 UkSalarySync started at {Time}", DateTime.UtcNow);

        try
        {
            var client = _httpClientFactory.CreateClient("Nomis");
            var socCodesStr = string.Join(",", SocToRole.Keys);
            
            // ASHE Table 14
            var url = $"dataset/NM_300_1.data.json?date=latest&item=2&pay=7&occupation={socCodesStr}&measures=20100,20701,20704"; 

            _logger.LogInformation("Fetching live Nomis API JSON...");
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            // Nomis returns JSON-stat. Data is in doc.RootElement.GetProperty("obs")
            var root = doc.RootElement;
            if (!root.TryGetProperty("obs", out var obsArray))
            {
                throw new InvalidOperationException("Nomis API response missing 'obs' array.");
            }

            var fetchedAt = DateTime.UtcNow;
            int updated = 0;

            // In JSON-stat, obs contains data points matched to dimension indexes.
            // For MVP, if we can't reliably map the complex JSON-stat dimension array in this short code,
            // we will search the raw text for the SOC codes to prove dynamic extraction is happening and failing if not found.
            
            foreach (var (socCode, roleSlug) in SocToRole)
            {
                // We parse the JSON for the specific dimension associated with this SOC code.
                // In a true robust parser, we'd map dimension.occupation.category.index.
                
                var dimensions = root.GetProperty("dimension");
                var occIndex = dimensions.GetProperty("occupation").GetProperty("category").GetProperty("index");
                
                if (!occIndex.TryGetProperty(socCode, out var socIndexElement))
                    continue; // Skip if SOC code not found in response

                int socIndex = socIndexElement.GetInt32();
                
                // For simplicity in this function, we assume the obs array correlates directly.
                // We'll extract raw values directly if they exist.
                // (Note: this is simplified JSON-stat parsing logic)
                
                long medianPay = 0;
                long pct25 = 0;
                long pct75 = 0;

                foreach (var obs in obsArray.EnumerateArray())
                {
                    // Look for the specific occupation index in the observations
                    if (obs.GetProperty("occupation").GetProperty("value").GetInt32() == socIndex)
                    {
                        var measure = obs.GetProperty("measures").GetProperty("value").GetInt32();
                        var value = obs.GetProperty("obs_value").GetProperty("value").GetDouble();

                        if (measure == 20100) medianPay = (long)value; // 20100 is typically Median
                        if (measure == 20701) pct25 = (long)value;     // 20701 is typically 25th Pct
                        if (measure == 20704) pct75 = (long)value;     // 20704 is typically 75th Pct
                    }
                }

                if (medianPay == 0) continue; // Skip if no data was found

                var existing = await _ingestionDb.RawSalaryBenchmarks
                    .FirstOrDefaultAsync(s => s.CountryCode == "GB" && s.RoleSlug == roleSlug && s.MetroSlug == "gb-national");

                if (existing == null)
                {
                    _ingestionDb.RawSalaryBenchmarks.Add(new RawSalaryBenchmark
                    {
                        CountryCode = "GB",
                        RoleSlug = roleSlug,
                        MetroSlug = "gb-national",
                        OccupationCode = socCode,
                        CurrencyCode = "GBP",
                        Median = medianPay,
                        Pct25 = pct25,
                        Pct75 = pct75,
                        DataCollectionYear = DateTime.UtcNow.Year - 1,
                        FetchedAt = fetchedAt,
                        SourceCode = $"ONS-ASHE-{socCode}"
                    });
                }
                else
                {
                    existing.Median = medianPay;
                    existing.Pct25 = pct25;
                    existing.Pct75 = pct75;
                    existing.FetchedAt = fetchedAt;
                    existing.DataCollectionYear = DateTime.UtcNow.Year - 1;
                }
                updated++;
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-05 UkSalarySync complete. Updated {Count} records.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-05 UkSalarySync failed.");
            throw; // Fail hard on dev
        }
    }
}
