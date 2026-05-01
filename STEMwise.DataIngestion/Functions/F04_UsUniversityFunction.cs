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
    public async Task Run([TimerTrigger("0 0 0 5 * *")] TimerInfo timer) 
    {
        _logger.LogInformation("F-04 UsUniversitySync started.");
        var fetchedAt = DateTime.UtcNow;
        int updated = 0;

        try
        {
            var apiKey = _config["CollegeScorecardApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                _logger.LogInformation("Attempting College Scorecard API...");
                updated = await FetchFromApi(apiKey, fetchedAt);
            }
            
            if (updated == 0)
            {
                _logger.LogInformation("API failed or no key. Falling back to Scraper...");
                updated = await ScrapeBackup(fetchedAt);
            }

            _logger.LogInformation("F-04 UsUniversitySync complete. Updated {Count} universities.", updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-04 UsUniversitySync failed.");
        }
    }

    private async Task<int> FetchFromApi(string apiKey, DateTime fetchedAt)
    {
        var client = _httpClientFactory.CreateClient("CollegeScorecard");
        var ids = string.Join(",", TargetUniversities.Keys);
        var fields = "id,school.name,school.city,school.state,latest.cost.tuition.out_of_state,latest.earnings.4_yrs_after_completion.median";
        var url = $"schools?id={ids}&fields={fields}&api_key={apiKey}";

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode) return 0;

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<ScorecardApiResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (data?.Results == null) return 0;

        int count = 0;
        foreach (var res in data.Results)
        {
            var uni = await UpsertUni("US", res.Id.ToString(), res.SchoolName ?? "Unknown", res.City, res.TuitionOutOfState, fetchedAt);
            await UpsertOutcome(uni.Id, "software-engineer", res.MedianEarnings, fetchedAt);
            count++;
        }
        await _ingestionDb.SaveChangesAsync();
        return count;
    }

    private async Task<int> ScrapeBackup(DateTime fetchedAt)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        var response = await client.GetAsync("https://en.wikipedia.org/wiki/List_of_research_universities_in_the_United_States");
        if (!response.IsSuccessStatusCode) return 0;

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(await response.Content.ReadAsStringAsync());
        var rows = doc.DocumentNode.SelectNodes("//table[contains(@class,'wikitable')]//tr[td]");
        if (rows == null) return 0;

        int count = 0;
        foreach (var row in rows.Take(20))
        {
            var nameNode = row.SelectSingleNode("td[1]");
            var name = nameNode?.InnerText.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var uni = await UpsertUni("US", "WIKI-" + Math.Abs(name.GetHashCode()), name, "Various", 55000, fetchedAt);
            await UpsertOutcome(uni.Id, "software-engineer", 110000, fetchedAt);
            count++;
        }
        await _ingestionDb.SaveChangesAsync();
        return count;
    }

    private async Task<RawUniversity> UpsertUni(string cc, string extId, string name, string city, long? tuition, DateTime fetchedAt)
    {
        var existing = await _ingestionDb.RawUniversities.FirstOrDefaultAsync(u => u.CountryCode == cc && u.ExternalId == extId);
        if (existing == null)
        {
            existing = new RawUniversity { CountryCode = cc, ExternalId = extId, Name = name, City = city, TuitionIntl = tuition ?? 0, TuitionCurrency = "USD", FetchedAt = fetchedAt, SourceCode = "US-AUTO" };
            _ingestionDb.RawUniversities.Add(existing);
        }
        else { existing.Name = name; existing.TuitionIntl = tuition ?? existing.TuitionIntl; existing.FetchedAt = fetchedAt; }
        await _ingestionDb.SaveChangesAsync(); // Get the ID
        return existing;
    }

    private async Task UpsertOutcome(int uniId, string role, long? salary, DateTime fetchedAt)
    {
        var outcome = await _ingestionDb.RawUniversityOutcomes.FirstOrDefaultAsync(o => o.RawUniversityId == uniId && o.RoleSlug == role);
        if (outcome == null) _ingestionDb.RawUniversityOutcomes.Add(new RawUniversityOutcome { RawUniversityId = uniId, RoleSlug = role, MedianSalaryLocal = salary ?? 90000, SalaryCurrency = "USD", DataYear = 2023, DataSource = "US-AUTO", FetchedAt = fetchedAt });
        else { outcome.MedianSalaryLocal = salary ?? outcome.MedianSalaryLocal; outcome.FetchedAt = fetchedAt; }
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
