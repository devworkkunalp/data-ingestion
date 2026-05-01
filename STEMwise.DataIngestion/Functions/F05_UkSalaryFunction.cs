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
        _logger.LogInformation("F-05 UkSalarySync started.");

        try
        {
            var client = _httpClientFactory.CreateClient("Nomis");
            var socCodesStr = string.Join(",", SocToRole.Keys);
            var url = $"dataset/NM_300_1.data.json?date=latest&item=2&pay=7&occupation={socCodesStr}&measures=20100,20701,20704"; 

            _logger.LogInformation("Fetching live Nomis API JSON...");
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("obs", out var obsElement))
            {
                _logger.LogWarning("No 'obs' property found in Nomis JSON.");
                return;
            }

            var dimensions = root.GetProperty("dimension");
            var occCats = dimensions.GetProperty("occupation").GetProperty("category").GetProperty("index");

            var fetchedAt = DateTime.UtcNow;
            int updated = 0;

            foreach (var (socCode, roleSlug) in SocToRole)
            {
                if (!occCats.TryGetProperty(socCode, out var socIndexElement)) continue;
                int socIndex = socIndexElement.GetInt32();

                long medianPay = 0, pct25 = 0, pct75 = 0;

                if (obsElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var obs in obsElement.EnumerateArray())
                {
                    try {
                        int occ = obs.GetProperty("occupation").GetProperty("value").GetInt32();
                        if (occ != socIndex) continue;

                        int measure = obs.GetProperty("measures").GetProperty("value").GetInt32();
                        var valProp = obs.GetProperty("obs_value").GetProperty("value");
                        double val = valProp.ValueKind == JsonValueKind.Number ? valProp.GetDouble() : 0;

                        if (measure == 20100) medianPay = (long)val;
                        if (measure == 20701) pct25 = (long)val;
                        if (measure == 20704) pct75 = (long)val;
                    } catch { continue; }
                }

                if (medianPay > 0)
                {
                    _logger.LogInformation("Found salary for {Role}: {Median} GBP", roleSlug, medianPay);
                    var existing = await _ingestionDb.RawSalaryBenchmarks
                        .FirstOrDefaultAsync(s => s.CountryCode == "GB" && s.RoleSlug == roleSlug && s.MetroSlug == "gb-national");

                    if (existing == null)
                    {
                        _ingestionDb.RawSalaryBenchmarks.Add(new RawSalaryBenchmark {
                            CountryCode = "GB", RoleSlug = roleSlug, MetroSlug = "gb-national",
                            OccupationCode = socCode, CurrencyCode = "GBP", Median = medianPay,
                            Pct25 = pct25, Pct75 = pct75, DataCollectionYear = 2023,
                            FetchedAt = fetchedAt, SourceCode = "ONS-ASHE"
                        });
                    }
                    else {
                        existing.Median = medianPay; existing.Pct25 = pct25; existing.Pct75 = pct75;
                        existing.FetchedAt = fetchedAt;
                    }
                    updated++;
                }
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-05 UkSalarySync complete. Updated {Count} records.", updated);
        }
        catch (Exception ex) { _logger.LogError(ex, "F-05 UkSalarySync failed."); }
    }
}
