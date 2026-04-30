namespace STEMwise.DataIngestion.Models;

/// <summary>
/// Raw FX rate as fetched from ExchangeRate-API.
/// Stored in orchestratorDB — source of truth for all currency conversions.
/// </summary>
public class RawFxRate
{
    public int Id { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;   // e.g. "USD"
    public string TargetCurrency { get; set; } = string.Empty; // e.g. "INR"
    public decimal Rate { get; set; }
    public DateTime FetchedAt { get; set; }
    public string SourceApi { get; set; } = "exchangerate-api.com";
}

/// <summary>
/// Raw salary benchmark as received from government source.
/// Values stored in LOCAL currency — never pre-converted.
/// e.g. UK salaries in GBP, Japan in JPY, Canada in CAD.
/// </summary>
public class RawSalaryBenchmark
{
    public int Id { get; set; }
    public string CountryCode { get; set; } = string.Empty;    // "US","GB","CA","AU","JP"
    public string RoleSlug { get; set; } = string.Empty;       // "software-engineer"
    public string MetroSlug { get; set; } = string.Empty;      // "us-san-francisco", "gb-london"
    public string OccupationCode { get; set; } = string.Empty; // SOC/NOC/ANZSCO/JSOC code
    public string CurrencyCode { get; set; } = string.Empty;   // "USD","GBP","CAD","AUD","JPY"
    public long? Pct10 { get; set; }
    public long? Pct25 { get; set; }
    public long Median { get; set; }
    public long? Pct75 { get; set; }
    public long? Pct90 { get; set; }
    public decimal? EmploymentRatePct { get; set; }
    public int DataCollectionYear { get; set; }   // Year the survey covers (not fetch year)
    public DateTime FetchedAt { get; set; }
    public string SourceCode { get; set; } = string.Empty;     // "BLS-OES-15-1252", "ONS-ASHE-2134"
}

/// <summary>
/// Raw university record as ingested from government source.
/// </summary>
public class RawUniversity
{
    public int Id { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;     // IPEDS/UKPRN/COPPID/PRV/NIAD
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? MetroSlug { get; set; }
    public long? TuitionIntl { get; set; }                     // In local currency
    public string? TuitionCurrency { get; set; }
    public bool IsJSkipDesignated { get; set; }                // Japan J-Skip only
    public DateTime FetchedAt { get; set; }
    public string SourceCode { get; set; } = string.Empty;
}

/// <summary>
/// Raw graduate outcome data per university, per role.
/// Salary in local currency — normalized at query time.
/// </summary>
public class RawUniversityOutcome
{
    public int Id { get; set; }
    public int RawUniversityId { get; set; }
    public RawUniversity? University { get; set; }
    public string RoleSlug { get; set; } = string.Empty;
    public long? MedianSalaryLocal { get; set; }
    public string? SalaryCurrency { get; set; }
    public decimal? EmploymentRatePct { get; set; }
    public decimal? VisaSuccessRatePct { get; set; }
    public decimal? LoanDefaultRatePct { get; set; }
    public int DataYear { get; set; }
    public string DataSource { get; set; } = string.Empty;     // "HESA-GOS-2023","QILT-2024"
    public DateTime FetchedAt { get; set; }
}

/// <summary>
/// Raw H-1B LCA disclosure record from DOL quarterly XLSX.
/// </summary>
public class RawH1bRecord
{
    public int Id { get; set; }
    public string EmployerName { get; set; } = string.Empty;
    public string SocCode { get; set; } = string.Empty;
    public string SocTitle { get; set; } = string.Empty;
    public int WageLevel { get; set; }                         // 1, 2, 3, or 4
    public string WorksiteCity { get; set; } = string.Empty;
    public string WorksiteState { get; set; } = string.Empty;
    public decimal PrevailingWage { get; set; }
    public string CaseStatus { get; set; } = string.Empty;    // "Certified","Withdrawn"
    public string Quarter { get; set; } = string.Empty;       // "FY2025-Q2"
    public DateTime FetchedAt { get; set; }
}

/// <summary>
/// Dynamic visa metrics extracted from government APIs (e.g. Express Entry CRS cutoffs, Skilled Worker thresholds).
/// </summary>
public class RawVisaMetric
{
    public int Id { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string VisaCategory { get; set; } = string.Empty; // "ExpressEntry", "SkilledWorker"
    public string MetricName { get; set; } = string.Empty; // "CRSCutoff", "MinimumSalary"
    public decimal MetricValue { get; set; }
    public DateTime FetchedAt { get; set; }
    public string SourceCode { get; set; } = string.Empty;
}

/// <summary>
/// Salary growth rate (CAGR) used by the projection engine.
/// Calculated from multi-year raw salary data.
/// </summary>
public class SalaryGrowthRate
{
    public int Id { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string? RoleSlug { get; set; }                     // NULL = applies to all roles in country
    public decimal AnnualGrowthRate { get; set; }             // e.g. 0.042 = 4.2%
    public int GrowthPeriodFrom { get; set; }
    public int GrowthPeriodTo { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Country configuration — drives all country-specific logic.
/// </summary>
public class Country
{
    public string Code { get; set; } = string.Empty;          // "US","GB","CA","AU","JP"
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;      // "USD","GBP","CAD","AUD","JPY"
    public string OccupationSystem { get; set; } = string.Empty; // "SOC-2018","NOC-2021"
    public string SalaryReportingFreq { get; set; } = string.Empty;
    public string SalaryPeriod { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int LaunchOrder { get; set; }
}
