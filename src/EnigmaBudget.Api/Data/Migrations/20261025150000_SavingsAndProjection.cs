using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SavingsAndProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancePayments_WorkspaceId_Id",
                table: "FinancePayments");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_WorkspaceId",
                table: "Accounts");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_FinancePayments_WorkspaceId_Id",
                table: "FinancePayments",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Accounts_WorkspaceId_Id",
                table: "Accounts",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.CreateTable(
                name: "ProjectionScenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionScenarios", x => x.Id);
                    table.UniqueConstraint("AK_ProjectionScenarios_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProjectionScenarios_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectionScenarios_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavingsCommands",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsCommands", x => new { x.WorkspaceId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_SavingsCommands_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavingsGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TargetCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TargetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DesiredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MonthlyContribution = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    NextContributionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsGoals", x => x.Id);
                    table.UniqueConstraint("AK_SavingsGoals_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_SavingsGoals_TargetAmount_Positive", "\"TargetAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_SavingsGoals_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectionAssumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionAssumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectionAssumptions_ProjectionScenarios_WorkspaceId_Scena~",
                        columns: x => new { x.WorkspaceId, x.ScenarioId },
                        principalTable: "ProjectionScenarios",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectionAssumptions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavingsAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReservedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsAllocations", x => x.Id);
                    table.UniqueConstraint("AK_SavingsAllocations_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_SavingsAllocations_ReservedAmount_NonNegative", "\"ReservedAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_SavingsAllocations_Accounts_WorkspaceId_AccountId",
                        columns: x => new { x.WorkspaceId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavingsAllocations_SavingsGoals_WorkspaceId_GoalId",
                        columns: x => new { x.WorkspaceId, x.GoalId },
                        principalTable: "SavingsGoals",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavingsAllocations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavingsMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FinancePaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsMovements", x => x.Id);
                    table.UniqueConstraint("AK_SavingsMovements_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_SavingsMovements_Amount_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_SavingsMovements_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavingsMovements_FinancePayments_WorkspaceId_FinancePayment~",
                        columns: x => new { x.WorkspaceId, x.FinancePaymentId },
                        principalTable: "FinancePayments",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavingsMovements_SavingsAllocations_WorkspaceId_AllocationId",
                        columns: x => new { x.WorkspaceId, x.AllocationId },
                        principalTable: "SavingsAllocations",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavingsMovements_SavingsCommands_WorkspaceId_CommandId",
                        columns: x => new { x.WorkspaceId, x.CommandId },
                        principalTable: "SavingsCommands",
                        principalColumns: new[] { "WorkspaceId", "RequestId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavingsMovements_SavingsMovements_WorkspaceId_ReversesMovem~",
                        columns: x => new { x.WorkspaceId, x.ReversesMovementId },
                        principalTable: "SavingsMovements",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavingsMovements_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionAssumptions_WorkspaceId_ScenarioId",
                table: "ProjectionAssumptions",
                columns: new[] { "WorkspaceId", "ScenarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionScenarios_ActorUserId",
                table: "ProjectionScenarios",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsAllocations_WorkspaceId_AccountId",
                table: "SavingsAllocations",
                columns: new[] { "WorkspaceId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SavingsAllocations_WorkspaceId_GoalId",
                table: "SavingsAllocations",
                columns: new[] { "WorkspaceId", "GoalId" });

            migrationBuilder.CreateIndex(
                name: "IX_SavingsMovements_ActorUserId",
                table: "SavingsMovements",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsMovements_WorkspaceId_AllocationId",
                table: "SavingsMovements",
                columns: new[] { "WorkspaceId", "AllocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SavingsMovements_WorkspaceId_CommandId_AllocationId_Kind",
                table: "SavingsMovements",
                columns: new[] { "WorkspaceId", "CommandId", "AllocationId", "Kind" },
                unique: true,
                filter: "\"Kind\" <> 'Reversal'");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsMovements_WorkspaceId_FinancePaymentId",
                table: "SavingsMovements",
                columns: new[] { "WorkspaceId", "FinancePaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SavingsMovements_WorkspaceId_ReversesMovementId",
                table: "SavingsMovements",
                columns: new[] { "WorkspaceId", "ReversesMovementId" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION "RejectSavingsMovementMutation"() RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'Savings movement records are immutable';
                END;
                $function$;
                CREATE TRIGGER "TR_SavingsMovements_Immutable"
                BEFORE UPDATE OR DELETE ON "SavingsMovements"
                FOR EACH ROW EXECUTE FUNCTION "RejectSavingsMovementMutation"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_SavingsMovements_Immutable" ON "SavingsMovements";
                DROP FUNCTION IF EXISTS "RejectSavingsMovementMutation"();
                """);

            migrationBuilder.DropTable(
                name: "ProjectionAssumptions");

            migrationBuilder.DropTable(
                name: "SavingsMovements");

            migrationBuilder.DropTable(
                name: "ProjectionScenarios");

            migrationBuilder.DropTable(
                name: "SavingsAllocations");

            migrationBuilder.DropTable(
                name: "SavingsCommands");

            migrationBuilder.DropTable(
                name: "SavingsGoals");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_FinancePayments_WorkspaceId_Id",
                table: "FinancePayments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Accounts_WorkspaceId_Id",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_FinancePayments_WorkspaceId_Id",
                table: "FinancePayments",
                columns: new[] { "WorkspaceId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_WorkspaceId",
                table: "Accounts",
                column: "WorkspaceId");
        }
    }
}
