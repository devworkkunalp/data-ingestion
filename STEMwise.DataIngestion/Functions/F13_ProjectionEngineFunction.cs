using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Functions;

public class ProjectionEngineFunction
{
    private readonly IngestionDbContext _ingestionDb;
    private readonly ApiDbContext _apiDb;
    private readonly ILogger<ProjectionEngineFunction> _logger;

    public ProjectionEngineFunction(
        IngestionDbContext ingestionDb,
        ApiDbContext apiDb,
        ILogger<ProjectionEngineFunction> logger)
    {
        _ingestionDb = ingestionDb;
        _apiDb = apiDb;
        _logger = logger;
    }

    [Function("ProjectionEngineSync")]
    public async Task Run([TimerTrigger("0 0 7 * * *")] TimerInfo timer)
    {
        _logger.LogInformation("F-13 ProjectionEngineSync started at {Time}", DateTime.UtcNow);

        try
        {
            await ProcessSalariesAsync();
            await ProcessUniversitiesAsync();
            
            _logger.LogInformation("F-13 ProjectionEngineSync complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "F-13 ProjectionEngineSync failed");
        }
    }

    private async Task ProcessSalariesAsync()
    {
        var rawSalaries = await _ingestionDb.RawSalaryBenchmarks.ToListAsync();
        var growthRates = await _ingestionDb.SalaryGrowthRates.ToListAsync();
        var currentYear = DateTime.UtcNow.Year;
        int updated = 0;

        foreach (var raw in rawSalaries)
        {
            try 
            {
                var rate = growthRates.FirstOrDefault(g => g.CountryCode == raw.CountryCode && g.RoleSlug == raw.RoleSlug)
                           ?? growthRates.FirstOrDefault(g => g.CountryCode == raw.CountryCode && g.RoleSlug == null);

                decimal cagr = rate?.AnnualGrowthRate ?? 0.03m;
                int yearsToProject = Math.Max(0, currentYear - raw.DataCollectionYear);

                decimal multiplier = (decimal)Math.Pow(1 + (double)cagr, yearsToProject);
                long projectedMedian = (long)(raw.Median * multiplier);
                long projectedPct25 = raw.Pct25.HasValue ? (long)(raw.Pct25.Value * multiplier) : (long)(projectedMedian * 0.8);
                long projectedPct75 = raw.Pct75.HasValue ? (long)(raw.Pct75.Value * multiplier) : (long)(projectedMedian * 1.2);

                decimal uncertainty = yearsToProject switch { 0 or 1 => 0.05m, 2 => 0.10m, 3 => 0.15m, _ => 0.20m };
                string confidenceLevel = yearsToProject switch { 0 or 1 => "HIGH", 2 => "MEDIUM", _ => "LOW" };

                var existing = await _apiDb.SalaryProjections
                    .FirstOrDefaultAsync(s => s.CountryCode == raw.CountryCode && s.RoleSlug == raw.RoleSlug && s.MetroSlug == raw.MetroSlug);

                if (existing == null)
                {
                    _apiDb.SalaryProjections.Add(new SalaryProjection
                    {
                        CountryCode = raw.CountryCode, RoleSlug = raw.RoleSlug, MetroSlug = raw.MetroSlug,
                        CurrencyCode = raw.CurrencyCode, RawMedian = raw.Median, ProjectedMedian = projectedMedian,
                        ProjectedPct25 = projectedPct25, ProjectedPct75 = projectedPct75,
                        ConfidenceLow = (long)(projectedMedian * (1 - uncertainty)),
                        ConfidenceHigh = (long)(projectedMedian * (1 + uncertainty)),
                        ConfidenceLevel = confidenceLevel, DataAgeMonths = yearsToProject * 12, DataYear = raw.DataCollectionYear,
                        SourceNote = $"Projected using {(cagr*100):0.0}% CAGR", EntryLevelSalary = (long)(projectedMedian * 0.75m),
                        ProjectedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.ProjectedMedian = projectedMedian;
                    existing.ProjectedPct25 = projectedPct25;
                    existing.ProjectedPct75 = projectedPct75;
                    existing.ProjectedAt = DateTime.UtcNow;
                }
                updated++;
            }
            catch (Exception ex) { _logger.LogWarning("Skipping salary {Id}: {Msg}", raw.Id, ex.Message); }
        }

        try { await _apiDb.SaveChangesAsync(); } catch (Exception ex) { _logger.LogError("API Save failed for salaries: {Msg}", ex.Message); }
        _logger.LogInformation("F-13: Projected {Count} salaries.", updated);
    }

    private async Task ProcessUniversitiesAsync()
    {
        var rawUnis = await _ingestionDb.RawUniversities.ToListAsync();
        var rawOutcomes = await _ingestionDb.RawUniversityOutcomes.ToListAsync();
        var rawFx = await _ingestionDb.RawFxRates.Where(f => f.BaseCurrency == "USD").ToListAsync();
        
        // Safety: Prevent duplicate key crash in ToDictionary
        var fxRates = rawFx.GroupBy(f => f.TargetCurrency)
                           .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchedAt).First().Rate);
        
        foreach (var raw in rawUnis)
        {
            try 
            {
                long? tuitionUsd = (raw.TuitionIntl.HasValue && raw.TuitionCurrency == "USD") ? raw.TuitionIntl : 
                                   (raw.TuitionIntl.HasValue && fxRates.TryGetValue(raw.TuitionCurrency, out decimal r) && r > 0) ? (long)(raw.TuitionIntl.Value / r) : null;

                var apiUni = await _apiDb.Universities.FirstOrDefaultAsync(u => u.CountryCode == raw.CountryCode && u.ExternalId == raw.ExternalId);
                if (apiUni == null)
                {
                    _apiDb.Universities.Add(new University {
                        CountryCode = raw.CountryCode, ExternalId = raw.ExternalId, Name = raw.Name, City = raw.City,
                        TuitionIntlLocal = raw.TuitionIntl, TuitionIntlUsd = tuitionUsd, TuitionCurrency = raw.TuitionCurrency,
                        IsJSkipDesignated = raw.IsJSkipDesignated, LastUpdated = DateTime.UtcNow
                    });
                }
                else
                {
                    apiUni.TuitionIntlUsd = tuitionUsd;
                    apiUni.LastUpdated = DateTime.UtcNow;
                }
            } catch { }
        }
        try { await _apiDb.SaveChangesAsync(); } catch { }

        // Process Outcomes
        int outCount = 0;
        foreach (var rawOutcome in rawOutcomes)
        {
            try {
                var rawUni = rawUnis.FirstOrDefault(u => u.Id == rawOutcome.RawUniversityId);
                if (rawUni == null) continue;

                var apiUni = await _apiDb.Universities.FirstOrDefaultAsync(u => u.CountryCode == rawUni.CountryCode && u.ExternalId == rawUni.ExternalId);
                if (apiUni == null) continue;

                long? salaryUsd = (rawOutcome.MedianSalaryLocal.HasValue && fxRates.TryGetValue(rawOutcome.SalaryCurrency ?? "", out decimal r) && r > 0) 
                                ? (long)(rawOutcome.MedianSalaryLocal.Value / r) : rawOutcome.MedianSalaryLocal;

                var existingOut = await _apiDb.UniversityOutcomes.FirstOrDefaultAsync(o => o.UniversityId == apiUni.Id && o.RoleSlug == rawOutcome.RoleSlug);
                if (existingOut == null)
                {
                    _apiDb.UniversityOutcomes.Add(new UniversityOutcome {
                        UniversityId = apiUni.Id, RoleSlug = rawOutcome.RoleSlug, MedianSalaryLocal = rawOutcome.MedianSalaryLocal,
                        MedianSalaryUsd = salaryUsd, EmploymentRatePct = rawOutcome.EmploymentRatePct, DataYear = rawOutcome.DataYear,
                        DataSource = rawOutcome.DataSource, LastUpdated = DateTime.UtcNow
                    });
                }
                outCount++;
            } catch { }
        }
        try { await _apiDb.SaveChangesAsync(); } catch { }
        _logger.LogInformation("F-13: Processed {Count} outcomes.", outCount);
    }
}
