using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FraudDetectionService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AmountDeviationThreshold",
                table: "Policies",
                type: "REAL",
                nullable: false,
                defaultValue: 2.5);

            migrationBuilder.AddColumn<string>(
                name: "AnalysisStrategy",
                table: "Policies",
                type: "TEXT",
                nullable: false,
                defaultValue: "LegacyScore");

            migrationBuilder.AddColumn<int>(
                name: "BurstCount",
                table: "Policies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "BurstWindowSeconds",
                table: "Policies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "MinimumHistory",
                table: "Policies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "SharedIpUsers",
                table: "Policies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "AnalysisStrategy",
                table: "Evaluations",
                type: "TEXT",
                nullable: false,
                defaultValue: "LegacyScore");

            migrationBuilder.AddColumn<string>(
                name: "RiskFindingsJson",
                table: "Evaluations",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountDeviationThreshold",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "AnalysisStrategy",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "BurstCount",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "BurstWindowSeconds",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "MinimumHistory",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "SharedIpUsers",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "AnalysisStrategy",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "RiskFindingsJson",
                table: "Evaluations");
        }
    }
}
