using Microsoft.EntityFrameworkCore;
using STEMwise.DataIngestion.Models;

namespace STEMwise.DataIngestion.Data;

/// <summary>
/// Ingestion DB context — writes to orchestratorDB.
/// Contains raw data exactly as received from government sources.
/// Written by: Azure Functions ingestion jobs only.
/// Read by: Projection engine (transforms raw → API-ready).
/// </summary>
public class IngestionDbContext : DbContext
{
    public IngestionDbContext(DbContextOptions<IngestionDbContext> options) : base(options) { }

    public DbSet<RawFxRate> RawFxRates { get; set; }
    public DbSet<RawSalaryBenchmark> RawSalaryBenchmarks { get; set; }
    public DbSet<RawUniversity> RawUniversities { get; set; }
    public DbSet<RawUniversityOutcome> RawUniversityOutcomes { get; set; }
    public DbSet<RawH1bRecord> RawH1bRecords { get; set; }
    public DbSet<RawVisaMetric> RawVisaMetrics { get; set; }
    public DbSet<SalaryGrowthRate> SalaryGrowthRates { get; set; }
    public DbSet<Country> Countries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // FX Rates — unique per currency pair
        modelBuilder.Entity<RawFxRate>()
            .HasIndex(f => new { f.BaseCurrency, f.TargetCurrency })
            .IsUnique();

        // Salary benchmarks — unique per country+role+metro
        modelBuilder.Entity<RawSalaryBenchmark>()
            .HasIndex(s => new { s.CountryCode, s.RoleSlug, s.MetroSlug })
            .IsUnique();

        // University — unique per country+externalId
        modelBuilder.Entity<RawUniversity>()
            .HasIndex(u => new { u.CountryCode, u.ExternalId })
            .IsUnique();

        // Countries — seeded once
        modelBuilder.Entity<Country>().HasKey(c => c.Code);
        modelBuilder.Entity<Country>().HasData(
            new Country { Code = "US", Name = "United States", Currency = "USD", OccupationSystem = "SOC-2018", SalaryReportingFreq = "bimonthly", SalaryPeriod = "annual", IsActive = true, LaunchOrder = 1 },
            new Country { Code = "GB", Name = "United Kingdom", Currency = "GBP", OccupationSystem = "SOC-2020", SalaryReportingFreq = "annual", SalaryPeriod = "annual", IsActive = true, LaunchOrder = 2 },
            new Country { Code = "CA", Name = "Canada", Currency = "CAD", OccupationSystem = "NOC-2021", SalaryReportingFreq = "monthly", SalaryPeriod = "annual", IsActive = true, LaunchOrder = 3 },
            new Country { Code = "AU", Name = "Australia", Currency = "AUD", OccupationSystem = "ANZSCO-2013", SalaryReportingFreq = "annual", SalaryPeriod = "annual", IsActive = true, LaunchOrder = 4 },
            new Country { Code = "JP", Name = "Japan", Currency = "JPY", OccupationSystem = "JSOC-2011", SalaryReportingFreq = "annual", SalaryPeriod = "monthly", IsActive = true, LaunchOrder = 5 }
        );

        // Growth rates — default CAGR values seeded
        modelBuilder.Entity<SalaryGrowthRate>().HasData(
            // USA
            new SalaryGrowthRate { Id = 1, CountryCode = "US", RoleSlug = "software-engineer", AnnualGrowthRate = 0.048m, GrowthPeriodFrom = 2019, GrowthPeriodTo = 2024, Source = "BLS-OES-historical", LastUpdated = DateTime.UtcNow },
            new SalaryGrowthRate { Id = 2, CountryCode = "US", RoleSlug = "data-scientist", AnnualGrowthRate = 0.062m, GrowthPeriodFrom = 2019, GrowthPeriodTo = 2024, Source = "BLS-OES-historical", LastUpdated = DateTime.UtcNow },
            new SalaryGrowthRate { Id = 3, CountryCode = "US", RoleSlug = "cybersecurity-eng", AnnualGrowthRate = 0.051m, GrowthPeriodFrom = 2019, GrowthPeriodTo = 2024, Source = "BLS-OES-historical", LastUpdated = DateTime.UtcNow },
            new SalaryGrowthRate { Id = 4, CountryCode = "US", RoleSlug = "electrical-engineer", AnnualGrowthRate = 0.032m, GrowthPeriodFrom = 2019, GrowthPeriodTo = 2024, Source = "BLS-OES-historical", LastUpdated = DateTime.UtcNow },
            new SalaryGrowthRate { Id = 5, CountryCode = "US", RoleSlug = "ml-engineer", AnnualGrowthRate = 0.074m, GrowthPeriodFrom = 2019, GrowthPeriodTo = 2024, Source = "BLS-OES-historical", LastUpdated = DateTime.UtcNow },
            // UK
            new SalaryGrowthRate { Id = 6, CountryCode = "GB", RoleSlug = "software-engineer", AnnualGrowthRate = 0.042m, GrowthPeriodFrom = 2020, GrowthPeriodTo = 2025, Source = "ONS-ASHE-historical", LastUpdated = DateTime.UtcNow },
            new SalaryGrowthRate { Id = 7, CountryCode = "GB", RoleSlug = "data-scientist", AnnualGrowthRate = 0.058m, GrowthPeriodFrom = 2020, GrowthPeriodTo = 2025, Source = "ONS-ASHE-historical", LastUpdated = DateTime.UtcNow },
            new SalaryGrowthRate { Id = 8, CountryCode = "GB", RoleSlug = "cybersecurity-eng", AnnualGrowthRate = 0.049m, GrowthPeriodFrom = 2020, GrowthPeriodTo = 2025, Source = "ONS-ASHE-historical", LastUpdated = DateTime.UtcNow },
            new SalaryGrowthRate { Id = 9, CountryCode = "GB", RoleSlug = "electrical-engineer", AnnualGrowthRate = 0.031m, GrowthPeriodFrom = 2020, GrowthPeriodTo = 2025, Source = "ONS-ASHE-historical", LastUpdated = DateTime.UtcNow },
            // Canada
            new SalaryGrowthRate { Id = 10, CountryCode = "CA", RoleSlug = null, AnnualGrowthRate = 0.048m, GrowthPeriodFrom = 2020, GrowthPeriodTo = 2024, Source = "StatCan-LFS-historical", LastUpdated = DateTime.UtcNow },
            // Australia
            new SalaryGrowthRate { Id = 11, CountryCode = "AU", RoleSlug = "software-engineer", AnnualGrowthRate = 0.045m, GrowthPeriodFrom = 2020, GrowthPeriodTo = 2025, Source = "ABS-EEH-historical", LastUpdated = DateTime.UtcNow },
            new SalaryGrowthRate { Id = 12, CountryCode = "AU", RoleSlug = "data-scientist", AnnualGrowthRate = 0.060m, GrowthPeriodFrom = 2020, GrowthPeriodTo = 2025, Source = "ABS-EEH-historical", LastUpdated = DateTime.UtcNow },
            // Japan
            new SalaryGrowthRate { Id = 13, CountryCode = "JP", RoleSlug = null, AnnualGrowthRate = 0.037m, GrowthPeriodFrom = 2020, GrowthPeriodTo = 2024, Source = "MHLW-historical", LastUpdated = DateTime.UtcNow }
        );
    }
}
