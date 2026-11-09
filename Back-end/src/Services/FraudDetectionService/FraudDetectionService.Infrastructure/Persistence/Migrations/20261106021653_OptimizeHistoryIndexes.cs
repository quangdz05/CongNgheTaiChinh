using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FraudDetectionService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeHistoryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviews_TransactionId",
                table: "Reviews");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DeviceHash_Timestamp",
                table: "Transactions",
                columns: new[] { "DeviceHash", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_IpHash_Timestamp",
                table: "Transactions",
                columns: new[] { "IpHash", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_TransactionId_ReviewedAt",
                table: "Reviews",
                columns: new[] { "TransactionId", "ReviewedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_DeviceHash_Timestamp",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_IpHash_Timestamp",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_TransactionId_ReviewedAt",
                table: "Reviews");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_TransactionId",
                table: "Reviews",
                column: "TransactionId");
        }
    }
}
