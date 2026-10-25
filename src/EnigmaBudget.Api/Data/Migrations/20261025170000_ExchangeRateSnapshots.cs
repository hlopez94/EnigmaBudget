using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeRateSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeRateRefreshCommands",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateRefreshCommands", x => new { x.WorkspaceId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_ExchangeRateRefreshCommands_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRateSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    House = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Buy = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    Sell = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    ProviderUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateSnapshots", x => x.Id);
                    table.CheckConstraint("CK_ExchangeRateSnapshots_Usd", "\"Currency\" = 'USD' AND (\"Buy\" IS NULL OR \"Buy\" > 0) AND (\"Sell\" IS NULL OR \"Sell\" > 0) AND (\"Buy\" IS NOT NULL OR \"Sell\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "SavingsGoalRateChoices",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    House = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ManualArsPerUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    ManualReason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsGoalRateChoices", x => new { x.WorkspaceId, x.GoalId });
                    table.CheckConstraint("CK_SavingsGoalRateChoices_Mode", "(\"Mode\" = 'Manual' AND \"SnapshotId\" IS NULL AND \"House\" IS NULL AND \"Side\" IS NULL AND \"ManualArsPerUsd\" > 0 AND \"ManualReason\" IS NOT NULL) OR (\"Mode\" = 'Snapshot' AND \"SnapshotId\" IS NOT NULL AND \"House\" IS NOT NULL AND \"Side\" IN ('Compra','Venta') AND \"ManualArsPerUsd\" IS NULL AND \"ManualReason\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_SavingsGoalRateChoices_ExchangeRateSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "ExchangeRateSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavingsGoalRateChoices_SavingsGoals_WorkspaceId_GoalId",
                        columns: x => new { x.WorkspaceId, x.GoalId },
                        principalTable: "SavingsGoals",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SavingsGoalRateChoices_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRateSnapshots_Currency_House_ProviderUpdatedAt_Buy_~",
                table: "ExchangeRateSnapshots",
                columns: new[] { "Currency", "House", "ProviderUpdatedAt", "Buy", "Sell" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavingsGoalRateChoices_SnapshotId",
                table: "SavingsGoalRateChoices",
                column: "SnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRateRefreshCommands");

            migrationBuilder.DropTable(
                name: "SavingsGoalRateChoices");

            migrationBuilder.DropTable(
                name: "ExchangeRateSnapshots");
        }
    }
}
