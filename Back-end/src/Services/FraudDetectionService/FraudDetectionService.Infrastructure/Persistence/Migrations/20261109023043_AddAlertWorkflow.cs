using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FraudDetectionService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Priority = table.Column<string>(type: "TEXT", nullable: false),
                    PriorityReason = table.Column<string>(type: "TEXT", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    OriginalEvaluatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    Resolution = table.Column<string>(type: "TEXT", nullable: false),
                    ClosureReasonCode = table.Column<string>(type: "TEXT", nullable: true),
                    ClosureNotes = table.Column<string>(type: "TEXT", nullable: true),
                    ClosedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    WaitingType = table.Column<string>(type: "TEXT", nullable: true),
                    WaitingReason = table.Column<string>(type: "TEXT", nullable: true),
                    WaitingUntil = table.Column<long>(type: "INTEGER", nullable: true),
                    SlaPolicyVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    BudgetMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    SlaStartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DueAt = table.Column<long>(type: "INTEGER", nullable: false),
                    HandlingEpisode = table.Column<int>(type: "INTEGER", nullable: false),
                    EscalatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    EscalationReason = table.Column<string>(type: "TEXT", nullable: true),
                    EscalationEligibleAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.CheckConstraint("CK_Alert_Owner", "(Status = 'New' AND OwnerUserId IS NULL) OR (Status = 'Escalated') OR (Status IN ('Assigned','InProgress','Waiting','Closed') AND OwnerUserId IS NOT NULL)");
                    table.CheckConstraint("CK_Alert_Priority", "Priority IN ('P1','P2','P3')");
                    table.CheckConstraint("CK_Alert_State", "Status IN ('New','Assigned','InProgress','Waiting','Escalated','Closed')");
                    table.ForeignKey(
                        name: "FK_Alerts_Evaluations_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Evaluations",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Alerts_Users_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Alerts_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AlertCommandReceipts",
                columns: table => new
                {
                    AlertId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Command = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertCommandReceipts", x => new { x.AlertId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_AlertCommandReceipts_Alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "Alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AlertCommandReceipts_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AlertEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AlertId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    BeforeVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    HandlingEpisode = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: false),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertEvents_Alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "Alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AlertEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertCommandReceipts_ActorUserId",
                table: "AlertCommandReceipts",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_ActorUserId",
                table: "AlertEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_AlertId_HandlingEpisode",
                table: "AlertEvents",
                columns: new[] { "AlertId", "HandlingEpisode" },
                unique: true,
                filter: "Type = 'Escalated'");

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_AlertId_Version",
                table: "AlertEvents",
                columns: new[] { "AlertId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_ClosedByUserId",
                table: "Alerts",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_OwnerUserId_Status_DueAt",
                table: "Alerts",
                columns: new[] { "OwnerUserId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Status_DueAt",
                table: "Alerts",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Status_Priority_DueAt_Id",
                table: "Alerts",
                columns: new[] { "Status", "Priority", "DueAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_TransactionId",
                table: "Alerts",
                column: "TransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertCommandReceipts");

            migrationBuilder.DropTable(
                name: "AlertEvents");

            migrationBuilder.DropTable(
                name: "Alerts");
        }
    }
}
