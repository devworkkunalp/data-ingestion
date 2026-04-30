using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STEMwise.DataIngestion.Migrations.Ingestion
{
    /// <inheritdoc />
    public partial class AddVisaMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawVisaMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VisaCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetricName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetricValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawVisaMetrics", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 1,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7286));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 2,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7762));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 3,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7766));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 4,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7769));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 5,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7772));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 6,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7775));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 7,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7778));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 8,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7781));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 9,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7784));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 10,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7787));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 11,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7790));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 12,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7792));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 13,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 18, 7, 41, 421, DateTimeKind.Utc).AddTicks(7795));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawVisaMetrics");

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 1,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5379));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 2,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5679));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 3,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5682));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 4,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5685));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 5,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5687));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 6,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5689));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 7,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5691));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 8,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5694));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 9,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5696));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 10,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5699));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 11,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5701));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 12,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5703));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 13,
                column: "LastUpdated",
                value: new DateTime(2026, 4, 30, 12, 45, 6, 71, DateTimeKind.Utc).AddTicks(5705));
        }
    }
}
