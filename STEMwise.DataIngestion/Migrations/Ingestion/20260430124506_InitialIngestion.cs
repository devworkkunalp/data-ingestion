using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace STEMwise.DataIngestion.Migrations.Ingestion
{
    /// <inheritdoc />
    public partial class InitialIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccupationSystem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SalaryReportingFreq = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SalaryPeriod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LaunchOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "RawFxRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaseCurrency = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TargetCurrency = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceApi = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawFxRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawH1bRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SocCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SocTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WageLevel = table.Column<int>(type: "int", nullable: false),
                    WorksiteCity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorksiteState = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrevailingWage = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CaseStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quarter = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawH1bRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawSalaryBenchmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleSlug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MetroSlug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OccupationCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Pct10 = table.Column<long>(type: "bigint", nullable: true),
                    Pct25 = table.Column<long>(type: "bigint", nullable: true),
                    Median = table.Column<long>(type: "bigint", nullable: false),
                    Pct75 = table.Column<long>(type: "bigint", nullable: true),
                    Pct90 = table.Column<long>(type: "bigint", nullable: true),
                    EmploymentRatePct = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DataCollectionYear = table.Column<int>(type: "int", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawSalaryBenchmarks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawUniversities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MetroSlug = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TuitionIntl = table.Column<long>(type: "bigint", nullable: true),
                    TuitionCurrency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsJSkipDesignated = table.Column<bool>(type: "bit", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawUniversities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalaryGrowthRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleSlug = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnnualGrowthRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrowthPeriodFrom = table.Column<int>(type: "int", nullable: false),
                    GrowthPeriodTo = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryGrowthRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawUniversityOutcomes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RawUniversityId = table.Column<int>(type: "int", nullable: false),
                    RoleSlug = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MedianSalaryLocal = table.Column<long>(type: "bigint", nullable: true),
                    SalaryCurrency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmploymentRatePct = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    VisaSuccessRatePct = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LoanDefaultRatePct = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DataYear = table.Column<int>(type: "int", nullable: false),
                    DataSource = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawUniversityOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawUniversityOutcomes_RawUniversities_RawUniversityId",
                        column: x => x.RawUniversityId,
                        principalTable: "RawUniversities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Countries",
                columns: new[] { "Code", "Currency", "IsActive", "LaunchOrder", "Name", "OccupationSystem", "SalaryPeriod", "SalaryReportingFreq" },
                values: new object[,]
                {
                    { "AU", "AUD", true, 4, "Australia", "ANZSCO-2013", "annual", "annual" },
                    { "CA", "CAD", true, 3, "Canada", "NOC-2021", "annual", "monthly" },
                    { "GB", "GBP", true, 2, "United Kingdom", "SOC-2020", "annual", "annual" },
                    { "JP", "JPY", true, 5, "Japan", "JSOC-2011", "monthly", "annual" },
                    { "US", "USD", true, 1, "United States", "SOC-2018", "annual", "bimonthly" }
                });

            migrationBuilder.InsertData(
                table: "SalaryGrowthRates",
                columns: new[] { "Id", "AnnualGrowthRate", "CountryCode", "GrowthPeriodFrom", "GrowthPeriodTo", "LastUpdated", "RoleSlug", "Source" },
                values: new object[,]
                {
                    { 1, 0.048m, "US", 2019, 2024, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5379), "software-engineer", "BLS-OES-historical" },
                    { 2, 0.062m, "US", 2019, 2024, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5679), "data-scientist", "BLS-OES-historical" },
                    { 3, 0.051m, "US", 2019, 2024, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5682), "cybersecurity-eng", "BLS-OES-historical" },
                    { 4, 0.032m, "US", 2019, 2024, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5685), "electrical-engineer", "BLS-OES-historical" },
                    { 5, 0.074m, "US", 2019, 2024, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5687), "ml-engineer", "BLS-OES-historical" },
                    { 6, 0.042m, "GB", 2020, 2025, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5689), "software-engineer", "ONS-ASHE-historical" },
                    { 7, 0.058m, "GB", 2020, 2025, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5691), "data-scientist", "ONS-ASHE-historical" },
                    { 8, 0.049m, "GB", 2020, 2025, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5694), "cybersecurity-eng", "ONS-ASHE-historical" },
                    { 9, 0.031m, "GB", 2020, 2025, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5696), "electrical-engineer", "ONS-ASHE-historical" },
                    { 10, 0.048m, "CA", 2020, 2024, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5699), null, "StatCan-LFS-historical" },
                    { 11, 0.045m, "AU", 2020, 2025, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5701), "software-engineer", "ABS-EEH-historical" },
                    { 12, 0.060m, "AU", 2020, 2025, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5703), "data-scientist", "ABS-EEH-historical" },
                    { 13, 0.037m, "JP", 2020, 2024, new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5705), null, "MHLW-historical" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawFxRates_BaseCurrency_TargetCurrency",
                table: "RawFxRates",
                columns: new[] { "BaseCurrency", "TargetCurrency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawSalaryBenchmarks_CountryCode_RoleSlug_MetroSlug",
                table: "RawSalaryBenchmarks",
                columns: new[] { "CountryCode", "RoleSlug", "MetroSlug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawUniversities_CountryCode_ExternalId",
                table: "RawUniversities",
                columns: new[] { "CountryCode", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawUniversityOutcomes_RawUniversityId",
                table: "RawUniversityOutcomes",
                column: "RawUniversityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropTable(
                name: "RawFxRates");

            migrationBuilder.DropTable(
                name: "RawH1bRecords");

            migrationBuilder.DropTable(
                name: "RawSalaryBenchmarks");

            migrationBuilder.DropTable(
                name: "RawUniversityOutcomes");

            migrationBuilder.DropTable(
                name: "SalaryGrowthRates");

            migrationBuilder.DropTable(
                name: "RawUniversities");
        }
    }
}
