using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STEMwise.DataIngestion.Data;

namespace STEMwise.DataIngestion.Functions;

/// <summary>
/// F-13: Salary Projection Engine
/// Runs: Daily at 07:00 UTC (after FX sync is complete)
/// Reads: orchestratorDB (raw data, growth rates, FX)
/// Writes: smtpwiseDB (display-ready salary projections and universities)
/// Logic: Applies CAGR up to current year, applies confidence band, normalizes to USD.
/// </summary>
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
            throw;
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
            // Find applicable growth rate (specific role or fallback country average)
            var rate = growthRates.FirstOrDefault(g => g.CountryCode == raw.CountryCode && g.RoleSlug == raw.RoleSlug)
                       ?? growthRates.FirstOrDefault(g => g.CountryCode == raw.CountryCode && g.RoleSlug == null);

            decimal cagr = rate?.AnnualGrowthRate ?? 0.03m; // Default 3% fallback

            int yearsToProject = currentYear - raw.DataCollectionYear;
            if (yearsToProject < 0) yearsToProject = 0;

            // CAGR projection
            decimal multiplier = (decimal)Math.Pow(1 + (double)cagr, yearsToProject);

            long projectedMedian = (long)(raw.Median * multiplier);
            long projectedPct25 = raw.Pct25.HasValue ? (long)(raw.Pct25.Value * multiplier) : (long)(projectedMedian * 0.8);
            long projectedPct75 = raw.Pct75.HasValue ? (long)(raw.Pct75.Value * multiplier) : (long)(projectedMedian * 1.2);

            // Confidence band based on data age
            decimal uncertainty = yearsToProject switch
            {
                0 or 1 => 0.05m, // 5%
                2 => 0.10m,      // 10%
                3 => 0.15m,      // 15%
                _ => 0.20m       // 20%
            };

            string confidenceLevel = yearsToProject switch
            {
                0 or 1 => "HIGH",
                2 => "MEDIUM",
                _ => "LOW"
            };

            long confLow = (long)(projectedMedian * (1 - uncertainty));
            long confHigh = (long)(projectedMedian * (1 + uncertainty));
            long entryLevel = (long)(projectedMedian * 0.75m); // Generalized entry-level factor

            var existing = await _apiDb.SalaryProjections
                .FirstOrDefaultAsync(s => 
                    s.CountryCode == raw.CountryCode && 
                    s.RoleSlug == raw.RoleSlug && 
                    s.MetroSlug == raw.MetroSlug);

            if (existing == null)
            {
                _apiDb.SalaryProjections.Add(new SalaryProjection
                {
                    CountryCode = raw.CountryCode,
                    RoleSlug = raw.RoleSlug,
                    MetroSlug = raw.MetroSlug,
                    CurrencyCode = raw.CurrencyCode,
                    RawMedian = raw.Median,
                    ProjectedMedian = projectedMedian,
                    ProjectedPct25 = projectedPct25,
                    ProjectedPct75 = projectedPct75,
                    ConfidenceLow = confLow,
                    ConfidenceHigh = confHigh,
                    ConfidenceLevel = confidenceLevel,
                    DataAgeMonths = yearsToProject * 12,
                    DataYear = raw.DataCollectionYear,
                    SourceNote = $"Projected from {raw.SourceCode} using {(cagr*100):0.0}% CAGR",
                    EntryLevelAdjNote = "Estimated at 75% of median",
                    EntryLevelSalary = entryLevel,
                    ProjectedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.RawMedian = raw.Median;
                existing.ProjectedMedian = projectedMedian;
                existing.ProjectedPct25 = projectedPct25;
                existing.ProjectedPct75 = projectedPct75;
                existing.ConfidenceLow = confLow;
                existing.ConfidenceHigh = confHigh;
                existing.ConfidenceLevel = confidenceLevel;
                existing.DataAgeMonths = yearsToProject * 12;
                existing.DataYear = raw.DataCollectionYear;
                existing.SourceNote = $"Projected from {raw.SourceCode} using {(cagr*100):0.0}% CAGR";
                existing.EntryLevelSalary = entryLevel;
                existing.ProjectedAt = DateTime.UtcNow;
            }
            
            updated++;
        }

        await _apiDb.SaveChangesAsync();
        _logger.LogInformation("Processed and projected {Count} salaries.", updated);
    }

    private async Task ProcessUniversitiesAsync()
    {
        var rawUnis = await _ingestionDb.RawUniversities.ToListAsync();
        var rawOutcomes = await _ingestionDb.RawUniversityOutcomes.ToListAsync();
        var fxRates = await _ingestionDb.RawFxRates.Where(f => f.BaseCurrency == "USD").ToDictionaryAsync(f => f.TargetCurrency, f => f.Rate);
        
        int updated = 0;

        foreach (var raw in rawUnis)
        {
            long? tuitionUsd = null;
            if (raw.TuitionIntl.HasValue && raw.TuitionCurrency == "USD")
            {
                tuitionUsd = raw.TuitionIntl;
            }
            else if (raw.TuitionIntl.HasValue && !string.IsNullOrEmpty(raw.TuitionCurrency) && fxRates.TryGetValue(raw.TuitionCurrency, out decimal rate) && rate > 0)
            {
                tuitionUsd = (long)(raw.TuitionIntl.Value / rate);
            }

            var apiUni = await _apiDb.Universities
                .FirstOrDefaultAsync(u => u.CountryCode == raw.CountryCode && u.ExternalId == raw.ExternalId);

            if (apiUni == null)
            {
                apiUni = new University
                {
                    CountryCode = raw.CountryCode,
                    ExternalId = raw.ExternalId,
                    Name = raw.Name,
                    City = raw.City,
                    MetroSlug = raw.MetroSlug,
                    TuitionIntlLocal = raw.TuitionIntl,
                    TuitionIntlUsd = tuitionUsd,
                    TuitionCurrency = raw.TuitionCurrency,
                    IsJSkipDesignated = raw.IsJSkipDesignated,
                    LastUpdated = DateTime.UtcNow
                };
                _apiDb.Universities.Add(apiUni);
            }
            else
            {
                apiUni.Name = raw.Name;
                apiUni.City = raw.City;
                apiUni.MetroSlug = raw.MetroSlug;
                apiUni.TuitionIntlLocal = raw.TuitionIntl;
                apiUni.TuitionIntlUsd = tuitionUsd;
                apiUni.TuitionCurrency = raw.TuitionCurrency;
                apiUni.IsJSkipDesignated = raw.IsJSkipDesignated;
                apiUni.LastUpdated = DateTime.UtcNow;
            }
        }

        await _apiDb.SaveChangesAsync();
        
        // Process outcomes
        foreach (var rawOutcome in rawOutcomes)
        {
            // We need to map RawUniversityId to apiDb UniversityId. 
            // Better to load them by ExternalId.
            var rawUni = rawUnis.FirstOrDefault(u => u.Id == rawOutcome.RawUniversityId);
            if (rawUni == null) continue;

            var apiUni = await _apiDb.Universities.FirstAsync(u => u.CountryCode == rawUni.CountryCode && u.ExternalId == rawUni.ExternalId);

            long? salaryUsd = null;
            if (rawOutcome.MedianSalaryLocal.HasValue && rawOutcome.SalaryCurrency == "USD")
            {
                salaryUsd = rawOutcome.MedianSalaryLocal;
            }
            else if (rawOutcome.MedianSalaryLocal.HasValue && !string.IsNullOrEmpty(rawOutcome.SalaryCurrency) && fxRates.TryGetValue(rawOutcome.SalaryCurrency, out decimal rate) && rate > 0)
            {
                salaryUsd = (long)(rawOutcome.MedianSalaryLocal.Value / rate);
            }
            
            int dataAge = (DateTime.UtcNow.Year - rawOutcome.DataYear) * 12;
            string confLevel = dataAge <= 24 ? "HIGH" : dataAge <= 48 ? "MEDIUM" : "LOW";

            var existingOut = await _apiDb.UniversityOutcomes
                .FirstOrDefaultAsync(o => o.UniversityId == apiUni.Id && o.RoleSlug == rawOutcome.RoleSlug);

            if (existingOut == null)
            {
                _apiDb.UniversityOutcomes.Add(new UniversityOutcome
                {
                    UniversityId = apiUni.Id,
                    RoleSlug = rawOutcome.RoleSlug,
                    MedianSalaryLocal = rawOutcome.MedianSalaryLocal,
                    MedianSalaryUsd = salaryUsd,
                    EmploymentRatePct = rawOutcome.EmploymentRatePct,
                    DataYear = rawOutcome.DataYear,
                    DataSource = rawOutcome.DataSource,
                    DataAgeMonths = dataAge,
                    ConfidenceLevel = confLevel,
                    LastUpdated = DateTime.UtcNow
                });
            }
            else
            {
                existingOut.MedianSalaryLocal = rawOutcome.MedianSalaryLocal;
                existingOut.MedianSalaryUsd = salaryUsd;
                existingOut.EmploymentRatePct = rawOutcome.EmploymentRatePct;
                existingOut.DataYear = rawOutcome.DataYear;
                existingOut.DataSource = rawOutcome.DataSource;
                existingOut.DataAgeMonths = dataAge;
                existingOut.ConfidenceLevel = confLevel;
                existingOut.LastUpdated = DateTime.UtcNow;
            }
            
            updated++;
        }

        await _apiDb.SaveChangesAsync();
        _logger.LogInformation("Processed {Count} university outcomes.", updated);
    }
}
