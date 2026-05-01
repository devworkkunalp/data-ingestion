using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STEMwise.DataIngestion.Migrations.Ingestion
{
    /// <inheritdoc />
    public partial class FixStaticDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 1,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 2,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 3,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 4,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 5,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 6,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 7,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 8,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 9,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 10,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 11,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 12,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SalaryGrowthRates",
                keyColumn: "Id",
                keyValue: 13,
                column: "LastUpdated",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
