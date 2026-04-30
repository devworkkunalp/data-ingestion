using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STEMwise.DataIngestion.Migrations.Api
{
    /// <inheritdoc />
    public partial class InitialApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FxRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromCurrency = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ToCurrency = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalaryProjections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleSlug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MetroSlug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawMedian = table.Column<long>(type: "bigint", nullable: false),
                    ProjectedMedian = table.Column<long>(type: "bigint", nullable: false),
                    ProjectedPct25 = table.Column<long>(type: "bigint", nullable: false),
                    ProjectedPct75 = table.Column<long>(type: "bigint", nullable: false),
                    ConfidenceLow = table.Column<long>(type: "bigint", nullable: false),
                    ConfidenceHigh = table.Column<long>(type: "bigint", nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataAgeMonths = table.Column<int>(type: "int", nullable: false),
                    DataYear = table.Column<int>(type: "int", nullable: false),
                    SourceNote = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntryLevelAdjNote = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntryLevelSalary = table.Column<long>(type: "bigint", nullable: false),
                    ProjectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryProjections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Universities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MetroSlug = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TuitionIntlUsd = table.Column<long>(type: "bigint", nullable: true),
                    TuitionIntlLocal = table.Column<long>(type: "bigint", nullable: true),
                    TuitionCurrency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsJSkipDesignated = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Universities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UniversityOutcomes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UniversityId = table.Column<int>(type: "int", nullable: false),
                    RoleSlug = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MedianSalaryUsd = table.Column<long>(type: "bigint", nullable: true),
                    MedianSalaryLocal = table.Column<long>(type: "bigint", nullable: true),
                    EmploymentRatePct = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DataYear = table.Column<int>(type: "int", nullable: false),
                    DataSource = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataAgeMonths = table.Column<int>(type: "int", nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UniversityOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UniversityOutcomes_Universities_UniversityId",
                        column: x => x.UniversityId,
                        principalTable: "Universities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_FromCurrency_ToCurrency",
                table: "FxRates",
                columns: new[] { "FromCurrency", "ToCurrency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryProjections_CountryCode_RoleSlug_MetroSlug",
                table: "SalaryProjections",
                columns: new[] { "CountryCode", "RoleSlug", "MetroSlug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Universities_CountryCode_ExternalId",
                table: "Universities",
                columns: new[] { "CountryCode", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UniversityOutcomes_UniversityId",
                table: "UniversityOutcomes",
                column: "UniversityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FxRates");

            migrationBuilder.DropTable(
                name: "SalaryProjections");

            migrationBuilder.DropTable(
                name: "UniversityOutcomes");

            migrationBuilder.DropTable(
                name: "Universities");
        }
    }
}
