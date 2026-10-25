using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinanceCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "StatementAdjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MerchantId",
                table: "Purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "FinanceOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IncomeSourceId",
                table: "FinanceOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MerchantId",
                table: "FinanceOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinanceCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceCategories", x => x.Id);
                    table.UniqueConstraint("AK_FinanceCategories_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_FinanceCategories_Kind", "\"Kind\" IN ('Expense','Income')");
                    table.ForeignKey(
                        name: "FK_FinanceCategories_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanceIncomeSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceIncomeSources", x => x.Id);
                    table.UniqueConstraint("AK_FinanceIncomeSources_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_FinanceIncomeSources_Kind", "\"Kind\" IN ('Salary','Extra','AnnualBonus','IndependentWork','Other')");
                    table.ForeignKey(
                        name: "FK_FinanceIncomeSources_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanceMerchants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceMerchants", x => x.Id);
                    table.UniqueConstraint("AK_FinanceMerchants_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_FinanceMerchants_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatementAdjustments_WorkspaceId_CategoryId",
                table: "StatementAdjustments",
                columns: new[] { "WorkspaceId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_WorkspaceId_CategoryId",
                table: "Purchases",
                columns: new[] { "WorkspaceId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_WorkspaceId_MerchantId",
                table: "Purchases",
                columns: new[] { "WorkspaceId", "MerchantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceOperations_WorkspaceId_CategoryId",
                table: "FinanceOperations",
                columns: new[] { "WorkspaceId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceOperations_WorkspaceId_IncomeSourceId",
                table: "FinanceOperations",
                columns: new[] { "WorkspaceId", "IncomeSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceOperations_WorkspaceId_MerchantId",
                table: "FinanceOperations",
                columns: new[] { "WorkspaceId", "MerchantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCategories_WorkspaceId_Kind_Name",
                table: "FinanceCategories",
                columns: new[] { "WorkspaceId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceIncomeSources_WorkspaceId_Kind_Name",
                table: "FinanceIncomeSources",
                columns: new[] { "WorkspaceId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceMerchants_WorkspaceId_Name",
                table: "FinanceMerchants",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceOperations_FinanceCategories_WorkspaceId_CategoryId",
                table: "FinanceOperations",
                columns: new[] { "WorkspaceId", "CategoryId" },
                principalTable: "FinanceCategories",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceOperations_FinanceIncomeSources_WorkspaceId_IncomeSo~",
                table: "FinanceOperations",
                columns: new[] { "WorkspaceId", "IncomeSourceId" },
                principalTable: "FinanceIncomeSources",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceOperations_FinanceMerchants_WorkspaceId_MerchantId",
                table: "FinanceOperations",
                columns: new[] { "WorkspaceId", "MerchantId" },
                principalTable: "FinanceMerchants",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_FinanceCategories_WorkspaceId_CategoryId",
                table: "Purchases",
                columns: new[] { "WorkspaceId", "CategoryId" },
                principalTable: "FinanceCategories",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_FinanceMerchants_WorkspaceId_MerchantId",
                table: "Purchases",
                columns: new[] { "WorkspaceId", "MerchantId" },
                principalTable: "FinanceMerchants",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StatementAdjustments_FinanceCategories_WorkspaceId_Category~",
                table: "StatementAdjustments",
                columns: new[] { "WorkspaceId", "CategoryId" },
                principalTable: "FinanceCategories",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                INSERT INTO "FinanceCategories" ("Id", "WorkspaceId", "Kind", "Name", "IsArchived", "Version")
                SELECT gen_random_uuid(), w."Id", kinds."Kind", 'Sin clasificar', FALSE, 1
                FROM "Workspaces" AS w
                CROSS JOIN (VALUES ('Expense'), ('Income')) AS kinds("Kind");

                INSERT INTO "FinanceCategories" ("Id", "WorkspaceId", "Kind", "Name", "IsArchived", "Version")
                SELECT DISTINCT gen_random_uuid(), legacy."WorkspaceId", legacy."Kind", legacy."Name", FALSE, 1
                FROM (
                    SELECT o."WorkspaceId", o."Kind", o."Category" AS "Name"
                    FROM "FinanceOperations" AS o
                    WHERE o."Kind" IN ('Expense', 'Income')
                      AND translate(o."Category", U&' \0009\000A\000B\000C\000D\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000', '') <> ''
                    UNION
                    SELECT p."WorkspaceId", 'Expense', p."Category"
                    FROM "Purchases" AS p
                    WHERE translate(p."Category", U&' \0009\000A\000B\000C\000D\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000', '') <> ''
                    UNION
                    SELECT a."WorkspaceId", 'Expense', a."Category"
                    FROM "StatementAdjustments" AS a
                    WHERE translate(a."Category", U&' \0009\000A\000B\000C\000D\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000', '') <> ''
                ) AS legacy
                ON CONFLICT ("WorkspaceId", "Kind", "Name") DO NOTHING;

                UPDATE "FinanceOperations" AS o
                SET "CategoryId" = c."Id"
                FROM "FinanceCategories" AS c
                WHERE o."Kind" IN ('Expense', 'Income')
                  AND c."WorkspaceId" = o."WorkspaceId"
                  AND c."Kind" = o."Kind"
                  AND c."Name" = CASE WHEN translate(o."Category", U&' \0009\000A\000B\000C\000D\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000', '') = '' THEN 'Sin clasificar' ELSE o."Category" END;

                UPDATE "Purchases" AS p
                SET "CategoryId" = c."Id"
                FROM "FinanceCategories" AS c
                WHERE c."WorkspaceId" = p."WorkspaceId"
                  AND c."Kind" = 'Expense'
                  AND c."Name" = CASE WHEN translate(p."Category", U&' \0009\000A\000B\000C\000D\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000', '') = '' THEN 'Sin clasificar' ELSE p."Category" END;

                UPDATE "StatementAdjustments" AS a
                SET "CategoryId" = c."Id"
                FROM "FinanceCategories" AS c
                WHERE c."WorkspaceId" = a."WorkspaceId"
                  AND c."Kind" = 'Expense'
                  AND c."Name" = CASE WHEN translate(a."Category", U&' \0009\000A\000B\000C\000D\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000', '') = '' THEN 'Sin clasificar' ELSE a."Category" END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinanceOperations_FinanceCategories_WorkspaceId_CategoryId",
                table: "FinanceOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_FinanceOperations_FinanceIncomeSources_WorkspaceId_IncomeSo~",
                table: "FinanceOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_FinanceOperations_FinanceMerchants_WorkspaceId_MerchantId",
                table: "FinanceOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_FinanceCategories_WorkspaceId_CategoryId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_FinanceMerchants_WorkspaceId_MerchantId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_StatementAdjustments_FinanceCategories_WorkspaceId_Category~",
                table: "StatementAdjustments");

            migrationBuilder.DropTable(
                name: "FinanceCategories");

            migrationBuilder.DropTable(
                name: "FinanceIncomeSources");

            migrationBuilder.DropTable(
                name: "FinanceMerchants");

            migrationBuilder.DropIndex(
                name: "IX_StatementAdjustments_WorkspaceId_CategoryId",
                table: "StatementAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_WorkspaceId_CategoryId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_WorkspaceId_MerchantId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_FinanceOperations_WorkspaceId_CategoryId",
                table: "FinanceOperations");

            migrationBuilder.DropIndex(
                name: "IX_FinanceOperations_WorkspaceId_IncomeSourceId",
                table: "FinanceOperations");

            migrationBuilder.DropIndex(
                name: "IX_FinanceOperations_WorkspaceId_MerchantId",
                table: "FinanceOperations");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "StatementAdjustments");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "MerchantId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "FinanceOperations");

            migrationBuilder.DropColumn(
                name: "IncomeSourceId",
                table: "FinanceOperations");

            migrationBuilder.DropColumn(
                name: "MerchantId",
                table: "FinanceOperations");
        }
    }
}
