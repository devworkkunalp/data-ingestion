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
/// F-04: USA Universities Pipeline (College Scorecard)
/// Runs: Monthly
/// Source: College Scorecard API (api.data.gov)
/// Writes to: orchestratorDB.RawUniversities & RawUniversityOutcomes
/// </summary>
public class UsUniversityFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionDbContext _ingestionDb;
    private readonly IConfiguration _config;
    private readonly ILogger<UsUniversityFunction> _logger;

    // A small subset of top US universities for STEMwise MVP
    private static readonly Dictionary<string, string> TargetUniversities = new()
    {
        { "166683", "Massachusetts Institute of Technology" },
        { "243744", "Stanford University" },
        { "110635", "University of California-Berkeley" },
        { "128902", "Carnegie Mellon University" },
        { "139940", "Georgia Institute of Technology" },
        { "145637", "University of Illinois Urbana-Champaign" },
        { "236939", "University of Washington" },
        { "228778", "The University of Texas at Austin" },
        { "190150", "Columbia University" },
        { "162928", "Johns Hopkins University" }
    };

    public UsUniversityFunction(
        IHttpClientFactory httpClientFactory,
        IngestionDbContext ingestionDb,
        IConfiguration config,
        ILogger<UsUniversityFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ingestionDb = ingestionDb;
        _config = config;
        _logger = logger;
    }

    [Function("UsUniversitySync")]
    public async Task Run(
        [TimerTrigger("0 0 0 5 * *")] TimerInfo timer) // 5th of every month
    {
        _logger.LogInformation("F-04 UsUniversitySync started at {Time}", DateTime.UtcNow);

        var apiKey = _config["CollegeScorecardApiKey"]
            ?? throw new InvalidOperationException("CollegeScorecardApiKey not configured");

        var client = _httpClientFactory.CreateClient("CollegeScorecard");
        var fetchedAt = DateTime.UtcNow;
        int updated = 0;

        try
        {
            // The API supports batching by ID, e.g., id=166683,243744...
            var ids = string.Join(",", TargetUniversities.Keys);
            
            // We request latest data. We want tuition and earnings
            // Note: out-of-state tuition represents international student baseline tuition.
            var fields = "id,school.name,school.city,school.state,latest.cost.tuition.out_of_state,latest.earnings.4_yrs_after_completion.median,latest.repayment.1_yr_repayment.all";
            
            var url = $"schools?id={ids}&fields={fields}&api_key={apiKey}";

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<ScorecardApiResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data?.Results == null)
            {
                _logger.LogWarning("Scorecard API returned no results.");
                return;
            }

            foreach (var result in data.Results)
            {
                var externalId = result.Id.ToString();
                
                var uni = await _ingestionDb.RawUniversities
                    .FirstOrDefaultAsync(u => u.CountryCode == "US" && u.ExternalId == externalId);

                if (uni == null)
                {
                    uni = new RawUniversity
                    {
                        CountryCode = "US",
                        ExternalId = externalId,
                        Name = result.SchoolName ?? "Unknown",
                        City = result.City,
                        TuitionIntl = result.TuitionOutOfState,
                        TuitionCurrency = "USD",
                        FetchedAt = fetchedAt,
                        SourceCode = "US-CollegeScorecard"
                    };
                    _ingestionDb.RawUniversities.Add(uni);
                }
                else
                {
                    uni.TuitionIntl = result.TuitionOutOfState;
                    uni.FetchedAt = fetchedAt;
                }

                // Wait for the DbContext to assign an ID if newly added, or we can just use navigation properties.
                // But it's easier to save changes first to get the ID, or just attach outcomes.
            }
            
            await _ingestionDb.SaveChangesAsync();

            // Now update outcomes
            foreach (var result in data.Results)
            {
                var externalId = result.Id.ToString();
                var uni = await _ingestionDb.RawUniversities
                    .FirstAsync(u => u.CountryCode == "US" && u.ExternalId == externalId);

                // Note: College Scorecard provides median earnings 4 years post grad, not necessarily split by role without deep program-level queries.
                // For MVP, we apply this general outcome to a generic STEM role or all roles.
                var outcome = await _ingestionDb.RawUniversityOutcomes
                    .FirstOrDefaultAsync(o => o.RawUniversityId == uni.Id && o.RoleSlug == "software-engineer");

                if (outcome == null)
                {
                    _ingestionDb.RawUniversityOutcomes.Add(new RawUniversityOutcome
                    {
                        RawUniversityId = uni.Id,
                        RoleSlug = "software-engineer",
                        MedianSalaryLocal = result.MedianEarnings,
                        SalaryCurrency = "USD",
                        LoanDefaultRatePct = result.RepaymentRate != null ? (1 - result.RepaymentRate.Value) * 100 : null,
                        DataYear = DateTime.UtcNow.Year - 4, // Approx since it's 4 yrs post-grad
                        DataSource = "US-CollegeScorecard",
                        FetchedAt = fetchedAt
                    });
                }
                else
                {
                    outcome.MedianSalaryLocal = result.MedianEarnings ?? outcome.MedianSalaryLocal;
                    outcome.FetchedAt = fetchedAt;
                }
                
                updated++;
            }

            await _ingestionDb.SaveChangesAsync();
            _logger.LogInformation("F-04 UsUniversitySync complete. Updated {Count} universities.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-04 UsUniversitySync failed");
            throw;
        }
    }
}

internal class ScorecardApiResponse
{
    public List<ScorecardResult>? Results { get; set; }
}

internal class ScorecardResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("school.name")]
    public string? SchoolName { get; set; }

    [JsonPropertyName("school.city")]
    public string? City { get; set; }

    [JsonPropertyName("school.state")]
    public string? State { get; set; }

    [JsonPropertyName("latest.cost.tuition.out_of_state")]
    public long? TuitionOutOfState { get; set; }

    [JsonPropertyName("latest.earnings.4_yrs_after_completion.median")]
    public long? MedianEarnings { get; set; }

    [JsonPropertyName("latest.repayment.1_yr_repayment.all")]
    public decimal? RepaymentRate { get; set; }
}
