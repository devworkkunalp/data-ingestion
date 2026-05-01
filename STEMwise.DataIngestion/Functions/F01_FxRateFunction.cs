using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-01: FX Rate Pipeline
/// Runs: Daily at 06:00 UTC (after ECB rates are published)
/// Source: ExchangeRate-API (v6) — 160+ currencies, free tier 1500 req/month
/// Writes to: orchestratorDB.RawFxRates (raw store)
///            smtpwiseDB.FxRates (API-ready store, same data)
/// Currencies: All pairs needed for student home-currency display
/// </summary>
public class FxRateFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly ApiDbContext _apiDb;
    private readonly IConfiguration _config;
    private readonly ILogger<FxRateFunction> _logger;

    // All currency pairs we need — covers all 5 study countries + top student home countries
    private static readonly string[] TargetCurrencies =
    [
        "GBP",  // UK
        "CAD",  // Canada
        "AUD",  // Australia
        "JPY",  // Japan
        "INR",  // India (largest international student source)
        "CNY",  // China
        "NGN",  // Nigeria
        "BRL",  // Brazil
        "PKR",  // Pakistan
        "BDT",  // Bangladesh
        "LKR",  // Sri Lanka
        "NPR",  // Nepal
        "KRW",  // South Korea
        "VND",  // Vietnam
        "EUR",  // EU students
    ];

    public FxRateFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        ApiDbContext apiDb,
        IConfiguration config,
        ILogger<FxRateFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _apiDb = apiDb;
        _config = config;
        _logger = logger;
    }

    [Function("FxRateSync")]
    public async Task Run(
        [TimerTrigger("0 0 6 * * *")] TimerInfo timer) // Daily 06:00 UTC
    {
        _logger.LogInformation("F-01 FxRateSync started at {Time}", DateTime.UtcNow);

        var apiKey = _config["ExchangeRateApiKey"]
            ?? throw new InvalidOperationException("ExchangeRateApiKey not configured");

        try
        {
            var client = _httpClientFactory.CreateClient("ExchangeRate");

            // Single call with USD as base — gets all rates in one request
            var url = $"{apiKey}/latest/USD";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<ExchangeRateApiResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data?.Result != "success" || data.ConversionRates == null)
            {
                _logger.LogError("ExchangeRate-API returned non-success: {Json}", json);
                return;
            }

            int updated = 0;
            var fetchedAt = DateTime.UtcNow;

            foreach (var targetCurrency in TargetCurrencies)
            {
                if (!data.ConversionRates.TryGetValue(targetCurrency, out var rate))
                {
                    _logger.LogWarning("Currency {Currency} not found in API response", targetCurrency);
                    continue;
                }

                // --- Write to orchestratorDB (raw store) ---
                var existing = await _ingestionDb.RawFxRates
                    .FirstOrDefaultAsync(f => f.BaseCurrency == "USD" && f.TargetCurrency == targetCurrency);

                if (existing == null)
                {
                    _ingestionDb.RawFxRates.Add(new RawFxRate
                    {
                        BaseCurrency = "USD",
                        TargetCurrency = targetCurrency,
                        Rate = rate,
                        FetchedAt = fetchedAt,
                        SourceApi = "exchangerate-api.com"
                    });
                }
                else
                {
                    existing.Rate = rate;
                    existing.FetchedAt = fetchedAt;
                }

                // --- Write to smtpwiseDB (API-ready store) ---
                var apiExisting = await _apiDb.FxRates
                    .FirstOrDefaultAsync(f => f.FromCurrency == "USD" && f.ToCurrency == targetCurrency);

                if (apiExisting == null)
                {
                    _apiDb.FxRates.Add(new FxRate
                    {
                        FromCurrency = "USD",
                        ToCurrency = targetCurrency,
                        Rate = rate,
                        LastUpdated = fetchedAt
                    });
                }
                else
                {
                    apiExisting.Rate = rate;
                    apiExisting.LastUpdated = fetchedAt;
                }

                updated++;
            }

            await _ingestionDb.SaveChangesAsync();
            await _apiDb.SaveChangesAsync();

            _logger.LogInformation("F-01 FxRateSync complete. {Count} rates updated.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-01 FxRateSync failed");
            throw;
        }
    }
}

// --- Response model for ExchangeRate-API v6 ---
internal class ExchangeRateApiResponse
{
    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    [JsonPropertyName("base_code")]
    public string BaseCode { get; set; } = string.Empty;

    [JsonPropertyName("conversion_rates")]
    public Dictionary<string, decimal>? ConversionRates { get; set; }
}
