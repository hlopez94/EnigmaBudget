using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinanceRecurrencesAndNonCashSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cards_WorkspaceId",
                table: "Cards");

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseOperationId",
                table: "Purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                table: "FinanceOperations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Purchases_WorkspaceId_Id",
                table: "Purchases",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_FinanceOperations_WorkspaceId_Id",
                table: "FinanceOperations",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Cards_WorkspaceId_Id",
                table: "Cards",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.CreateTable(
                name: "RecurringRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringRules", x => x.Id);
                    table.UniqueConstraint("AK_RecurringRules_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_RecurringRules_State", "\"State\" IN ('Active','Paused','Ended') AND \"CurrentVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_RecurringRules_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringRuleCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringRuleCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringRuleCommands_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringRuleCommands_RecurringRules_WorkspaceId_RuleId",
                        columns: x => new { x.WorkspaceId, x.RuleId },
                        principalTable: "RecurringRules",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringRuleCommands_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringRuleRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScheduleAnchorDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Flow = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncomeSourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDay = table.Column<int>(type: "integer", nullable: true),
                    PreferredAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreferredCardId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringRuleRevisions", x => x.Id);
                    table.UniqueConstraint("AK_RecurringRuleRevisions_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.UniqueConstraint("AK_RecurringRuleRevisions_WorkspaceId_RuleId_Id", x => new { x.WorkspaceId, x.RuleId, x.Id });
                    table.CheckConstraint("CK_RecurringRuleRevisions_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_RecurringRuleRevisions_Currency", "\"Currency\" IN ('ARS','USD')");
                    table.CheckConstraint("CK_RecurringRuleRevisions_DueDay", "\"DueDay\" IS NULL OR \"DueDay\" BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_RecurringRuleRevisions_FlowAndAmountMode", "\"Flow\" IN ('Income','Expense') AND \"AmountMode\" IN ('Fixed','Estimated')");
                    table.CheckConstraint("CK_RecurringRuleRevisions_Frequency", "\"Frequency\" IN ('Monthly','Weekly','Annual')");
                    table.CheckConstraint("CK_RecurringRuleRevisions_Interval", "\"Interval\" > 0");
                    table.CheckConstraint("CK_RecurringRuleRevisions_State", "\"State\" IN ('Active','Paused','Ended')");
                    table.CheckConstraint("CK_RecurringRuleRevisions_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_RecurringRuleRevisions_Accounts_WorkspaceId_PreferredAccoun~",
                        columns: x => new { x.WorkspaceId, x.PreferredAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringRuleRevisions_Cards_WorkspaceId_PreferredCardId",
                        columns: x => new { x.WorkspaceId, x.PreferredCardId },
                        principalTable: "Cards",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringRuleRevisions_FinanceCategories_WorkspaceId_Catego~",
                        columns: x => new { x.WorkspaceId, x.CategoryId },
                        principalTable: "FinanceCategories",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringRuleRevisions_FinanceIncomeSources_WorkspaceId_Inc~",
                        columns: x => new { x.WorkspaceId, x.IncomeSourceId },
                        principalTable: "FinanceIncomeSources",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringRuleRevisions_FinanceMerchants_WorkspaceId_Merchan~",
                        columns: x => new { x.WorkspaceId, x.MerchantId },
                        principalTable: "FinanceMerchants",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringRuleRevisions_RecurringRules_WorkspaceId_RuleId",
                        columns: x => new { x.WorkspaceId, x.RuleId },
                        principalTable: "RecurringRules",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringOccurrenceActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FinanceOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CardPurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringOccurrenceActions", x => x.Id);
                    table.UniqueConstraint("AK_RecurringOccurrenceActions_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.UniqueConstraint("AK_RecurringOccurrenceActions_WorkspaceId_Id_RuleRevisionId", x => new { x.WorkspaceId, x.Id, x.RuleRevisionId });
                    table.CheckConstraint("CK_RecurringOccurrenceActions_OutcomeSources", "(\"Outcome\" = 'Recorded' AND \"FinanceOperationId\" IS NOT NULL) OR (\"Outcome\" = 'Skipped' AND \"FinanceOperationId\" IS NULL AND \"CardPurchaseId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_RecurringOccurrenceActions_FinanceOperations_WorkspaceId_Fi~",
                        columns: x => new { x.WorkspaceId, x.FinanceOperationId },
                        principalTable: "FinanceOperations",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOccurrenceActions_Purchases_WorkspaceId_CardPurcha~",
                        columns: x => new { x.WorkspaceId, x.CardPurchaseId },
                        principalTable: "Purchases",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOccurrenceActions_RecurringRuleRevisions_Workspace~",
                        columns: x => new { x.WorkspaceId, x.RuleId, x.RuleRevisionId },
                        principalTable: "RecurringRuleRevisions",
                        principalColumns: new[] { "WorkspaceId", "RuleId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOccurrenceActions_RecurringRules_WorkspaceId_RuleId",
                        columns: x => new { x.WorkspaceId, x.RuleId },
                        principalTable: "RecurringRules",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealthcareComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurrenceActionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AppliesToProviderBalance = table.Column<bool>(type: "boolean", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthcareComponents", x => x.Id);
                    table.UniqueConstraint("AK_HealthcareComponents_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_HealthcareComponents_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_HealthcareComponents_AppliesToProviderBalance", "NOT \"AppliesToProviderBalance\" OR \"Kind\" = 'PayrollWithholding'");
                    table.CheckConstraint("CK_HealthcareComponents_Kind", "\"Kind\" IN ('ProviderCharge','ProviderDiscount','PayrollWithholding','HouseholdContribution','ExpectedRefund','Other')");
                    table.ForeignKey(
                        name: "FK_HealthcareComponents_RecurringOccurrenceActions_WorkspaceId~",
                        columns: x => new { x.WorkspaceId, x.OccurrenceActionId, x.RuleRevisionId },
                        principalTable: "RecurringOccurrenceActions",
                        principalColumns: new[] { "WorkspaceId", "Id", "RuleRevisionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealthcareComponents_RecurringRuleRevisions_WorkspaceId_Rul~",
                        columns: x => new { x.WorkspaceId, x.RuleRevisionId },
                        principalTable: "RecurringRuleRevisions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealthcareComponents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NonCashSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    HealthcareComponentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CardPurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesSettlementId = table.Column<Guid>(type: "uuid", nullable: true),
                    CauseVoidRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonCashSettlements", x => x.Id);
                    table.UniqueConstraint("AK_NonCashSettlements_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_NonCashSettlements_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_NonCashSettlements_Currency", "\"Currency\" IN ('ARS','USD')");
                    table.CheckConstraint("CK_NonCashSettlements_KindSource", "(\"Kind\" = 'PayrollWithholding' AND \"HealthcareComponentId\" IS NOT NULL AND \"CardPurchaseId\" IS NULL AND \"ReversesSettlementId\" IS NULL) OR (\"Kind\" = 'CardCharge' AND \"HealthcareComponentId\" IS NULL AND \"CardPurchaseId\" IS NOT NULL AND \"ReversesSettlementId\" IS NULL) OR (\"Kind\" = 'Reversal' AND \"HealthcareComponentId\" IS NULL AND \"CardPurchaseId\" IS NULL AND \"ReversesSettlementId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_NonCashSettlements_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonCashSettlements_FinanceOperations_WorkspaceId_OperationId",
                        columns: x => new { x.WorkspaceId, x.OperationId },
                        principalTable: "FinanceOperations",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonCashSettlements_HealthcareComponents_WorkspaceId_Healthc~",
                        columns: x => new { x.WorkspaceId, x.HealthcareComponentId },
                        principalTable: "HealthcareComponents",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonCashSettlements_NonCashSettlements_WorkspaceId_ReversesS~",
                        columns: x => new { x.WorkspaceId, x.ReversesSettlementId },
                        principalTable: "NonCashSettlements",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonCashSettlements_Purchases_WorkspaceId_CardPurchaseId",
                        columns: x => new { x.WorkspaceId, x.CardPurchaseId },
                        principalTable: "Purchases",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonCashSettlements_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_WorkspaceId_ExpenseOperationId",
                table: "Purchases",
                columns: new[] { "WorkspaceId", "ExpenseOperationId" },
                unique: true,
                filter: "\"ExpenseOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancePayments_WorkspaceId_AccountId",
                table: "FinancePayments",
                columns: new[] { "WorkspaceId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancePayments_WorkspaceId_OperationId",
                table: "FinancePayments",
                columns: new[] { "WorkspaceId", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceOperations_WorkspaceId_AccountId",
                table: "FinanceOperations",
                columns: new[] { "WorkspaceId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealthcareComponents_WorkspaceId_OccurrenceActionId_RuleRev~",
                table: "HealthcareComponents",
                columns: new[] { "WorkspaceId", "OccurrenceActionId", "RuleRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealthcareComponents_WorkspaceId_RuleRevisionId",
                table: "HealthcareComponents",
                columns: new[] { "WorkspaceId", "RuleRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_NonCashSettlements_ActorUserId",
                table: "NonCashSettlements",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NonCashSettlements_WorkspaceId_CardPurchaseId",
                table: "NonCashSettlements",
                columns: new[] { "WorkspaceId", "CardPurchaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_NonCashSettlements_WorkspaceId_HealthcareComponentId",
                table: "NonCashSettlements",
                columns: new[] { "WorkspaceId", "HealthcareComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_NonCashSettlements_WorkspaceId_OperationId",
                table: "NonCashSettlements",
                columns: new[] { "WorkspaceId", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_NonCashSettlements_WorkspaceId_RequestId",
                table: "NonCashSettlements",
                columns: new[] { "WorkspaceId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NonCashSettlements_WorkspaceId_ReversesSettlementId",
                table: "NonCashSettlements",
                columns: new[] { "WorkspaceId", "ReversesSettlementId" },
                unique: true,
                filter: "\"ReversesSettlementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrenceActions_WorkspaceId_CardPurchaseId",
                table: "RecurringOccurrenceActions",
                columns: new[] { "WorkspaceId", "CardPurchaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrenceActions_WorkspaceId_FinanceOperationId",
                table: "RecurringOccurrenceActions",
                columns: new[] { "WorkspaceId", "FinanceOperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrenceActions_WorkspaceId_RequestId",
                table: "RecurringOccurrenceActions",
                columns: new[] { "WorkspaceId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrenceActions_WorkspaceId_RuleId_RuleRevisionId",
                table: "RecurringOccurrenceActions",
                columns: new[] { "WorkspaceId", "RuleId", "RuleRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrenceActions_WorkspaceId_RuleId_ScheduledDate",
                table: "RecurringOccurrenceActions",
                columns: new[] { "WorkspaceId", "RuleId", "ScheduledDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleCommands_ActorUserId",
                table: "RecurringRuleCommands",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleCommands_WorkspaceId_RequestId",
                table: "RecurringRuleCommands",
                columns: new[] { "WorkspaceId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleCommands_WorkspaceId_RuleId",
                table: "RecurringRuleCommands",
                columns: new[] { "WorkspaceId", "RuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleRevisions_WorkspaceId_CategoryId",
                table: "RecurringRuleRevisions",
                columns: new[] { "WorkspaceId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleRevisions_WorkspaceId_IncomeSourceId",
                table: "RecurringRuleRevisions",
                columns: new[] { "WorkspaceId", "IncomeSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleRevisions_WorkspaceId_MerchantId",
                table: "RecurringRuleRevisions",
                columns: new[] { "WorkspaceId", "MerchantId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleRevisions_WorkspaceId_PreferredAccountId",
                table: "RecurringRuleRevisions",
                columns: new[] { "WorkspaceId", "PreferredAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleRevisions_WorkspaceId_PreferredCardId",
                table: "RecurringRuleRevisions",
                columns: new[] { "WorkspaceId", "PreferredCardId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleRevisions_WorkspaceId_RuleId_EffectiveFrom_Ver~",
                table: "RecurringRuleRevisions",
                columns: new[] { "WorkspaceId", "RuleId", "EffectiveFrom", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleRevisions_WorkspaceId_RuleId_Version",
                table: "RecurringRuleRevisions",
                columns: new[] { "WorkspaceId", "RuleId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_WorkspaceId_Id",
                table: "RecurringRules",
                columns: new[] { "WorkspaceId", "Id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceOperations_Accounts_WorkspaceId_AccountId",
                table: "FinanceOperations",
                columns: new[] { "WorkspaceId", "AccountId" },
                principalTable: "Accounts",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FinancePayments_Accounts_WorkspaceId_AccountId",
                table: "FinancePayments",
                columns: new[] { "WorkspaceId", "AccountId" },
                principalTable: "Accounts",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FinancePayments_FinanceOperations_WorkspaceId_OperationId",
                table: "FinancePayments",
                columns: new[] { "WorkspaceId", "OperationId" },
                principalTable: "FinanceOperations",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_FinanceOperations_WorkspaceId_ExpenseOperationId",
                table: "Purchases",
                columns: new[] { "WorkspaceId", "ExpenseOperationId" },
                principalTable: "FinanceOperations",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinanceOperations_Accounts_WorkspaceId_AccountId",
                table: "FinanceOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_FinancePayments_Accounts_WorkspaceId_AccountId",
                table: "FinancePayments");

            migrationBuilder.DropForeignKey(
                name: "FK_FinancePayments_FinanceOperations_WorkspaceId_OperationId",
                table: "FinancePayments");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_FinanceOperations_WorkspaceId_ExpenseOperationId",
                table: "Purchases");

            migrationBuilder.DropTable(
                name: "NonCashSettlements");

            migrationBuilder.DropTable(
                name: "RecurringRuleCommands");

            migrationBuilder.DropTable(
                name: "HealthcareComponents");

            migrationBuilder.DropTable(
                name: "RecurringOccurrenceActions");

            migrationBuilder.DropTable(
                name: "RecurringRuleRevisions");

            migrationBuilder.DropTable(
                name: "RecurringRules");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Purchases_WorkspaceId_Id",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_WorkspaceId_ExpenseOperationId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_FinancePayments_WorkspaceId_AccountId",
                table: "FinancePayments");

            migrationBuilder.DropIndex(
                name: "IX_FinancePayments_WorkspaceId_OperationId",
                table: "FinancePayments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_FinanceOperations_WorkspaceId_Id",
                table: "FinanceOperations");

            migrationBuilder.DropIndex(
                name: "IX_FinanceOperations_WorkspaceId_AccountId",
                table: "FinanceOperations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Cards_WorkspaceId_Id",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "ExpenseOperationId",
                table: "Purchases");

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                table: "FinanceOperations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cards_WorkspaceId",
                table: "Cards",
                column: "WorkspaceId");
        }
    }
}
