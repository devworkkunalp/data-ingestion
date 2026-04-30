using Microsoft.EntityFrameworkCore;

namespace STEMwise.DataIngestion.Data;

// -----------------------------------------------------------------
// API DB (smtpwiseDB) — processed display-ready data for frontend
// Schema mirrors exactly what the STEMwise.API reads
// Written by: Projection engine only
// Read by: STEMwise.API controllers
// -----------------------------------------------------------------

public class FxRate
{
    public int Id { get; set; }
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class SalaryProjection
{
    public int Id { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string RoleSlug { get; set; } = string.Empty;
    public string MetroSlug { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public long RawMedian { get; set; }              // Exact from government source
    public long ProjectedMedian { get; set; }         // Projected to today
    public long ProjectedPct25 { get; set; }
    public long ProjectedPct75 { get; set; }
    public long ConfidenceLow { get; set; }           // ProjectedMedian × (1 - uncertainty)
    public long ConfidenceHigh { get; set; }          // ProjectedMedian × (1 + uncertainty)
    public string ConfidenceLevel { get; set; } = string.Empty;  // HIGH/MEDIUM/LOW
    public int DataAgeMonths { get; set; }
    public int DataYear { get; set; }
    public string SourceNote { get; set; } = string.Empty;
    public string EntryLevelAdjNote { get; set; } = string.Empty;
    public long EntryLevelSalary { get; set; }        // ProjectedMedian × entry-level factor
    public DateTime ProjectedAt { get; set; }
}

public class University
{
    public int Id { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? MetroSlug { get; set; }
    public long? TuitionIntlUsd { get; set; }         // Always stored in USD for comparisons
    public long? TuitionIntlLocal { get; set; }        // In local currency
    public string? TuitionCurrency { get; set; }
    public bool IsJSkipDesignated { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class UniversityOutcome
{
    public int Id { get; set; }
    public int UniversityId { get; set; }
    public University? University { get; set; }
    public string RoleSlug { get; set; } = string.Empty;
    public long? MedianSalaryUsd { get; set; }        // Projected + converted to USD
    public long? MedianSalaryLocal { get; set; }       // Projected in local currency
    public decimal? EmploymentRatePct { get; set; }
    public int DataYear { get; set; }
    public string DataSource { get; set; } = string.Empty;
    public int DataAgeMonths { get; set; }
    public string ConfidenceLevel { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}

public class ApiDbContext : DbContext
{
    public ApiDbContext(DbContextOptions<ApiDbContext> options) : base(options) { }

    public DbSet<FxRate> FxRates { get; set; }
    public DbSet<SalaryProjection> SalaryProjections { get; set; }
    public DbSet<University> Universities { get; set; }
    public DbSet<UniversityOutcome> UniversityOutcomes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FxRate>()
            .HasIndex(f => new { f.FromCurrency, f.ToCurrency })
            .IsUnique();

        modelBuilder.Entity<SalaryProjection>()
            .HasIndex(s => new { s.CountryCode, s.RoleSlug, s.MetroSlug })
            .IsUnique();

        modelBuilder.Entity<University>()
            .HasIndex(u => new { u.CountryCode, u.ExternalId })
            .IsUnique();
    }
}
