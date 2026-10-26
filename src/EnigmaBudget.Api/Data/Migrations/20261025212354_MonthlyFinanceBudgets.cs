using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MonthlyFinanceBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonthlyBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyBudgets", x => x.Id);
                    table.UniqueConstraint("AK_MonthlyBudgets_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_MonthlyBudgets_Limit_NonNegative", "\"Limit\" >= 0");
                    table.ForeignKey(
                        name: "FK_MonthlyBudgets_FinanceCategories_WorkspaceId_CategoryId",
                        columns: x => new { x.WorkspaceId, x.CategoryId },
                        principalTable: "FinanceCategories",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MonthlyBudgets_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetCommands",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TargetBudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetCommands", x => new { x.WorkspaceId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_BudgetCommands_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetCommands_MonthlyBudgets_WorkspaceId_TargetBudgetId",
                        columns: x => new { x.WorkspaceId, x.TargetBudgetId },
                        principalTable: "MonthlyBudgets",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetCommands_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetCommands_ActorUserId",
                table: "BudgetCommands",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetCommands_WorkspaceId_TargetBudgetId",
                table: "BudgetCommands",
                columns: new[] { "WorkspaceId", "TargetBudgetId" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyBudgets_WorkspaceId_CategoryId",
                table: "MonthlyBudgets",
                columns: new[] { "WorkspaceId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyBudgets_WorkspaceId_Period_CategoryId_Currency",
                table: "MonthlyBudgets",
                columns: new[] { "WorkspaceId", "Period", "CategoryId", "Currency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetCommands");

            migrationBuilder.DropTable(
                name: "MonthlyBudgets");
        }
    }
}
