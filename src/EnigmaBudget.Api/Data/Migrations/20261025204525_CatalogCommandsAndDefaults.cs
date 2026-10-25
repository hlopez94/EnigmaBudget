using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CatalogCommandsAndDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "FinanceCategories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE \"FinanceCategories\" SET \"IsDefault\" = TRUE WHERE \"Name\" = 'Sin clasificar' AND \"Kind\" IN ('Expense', 'Income')");

            migrationBuilder.CreateTable(
                name: "CatalogCommands",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogCommands", x => new { x.WorkspaceId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_CatalogCommands_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CatalogCommands_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCategories_WorkspaceId_Kind",
                table: "FinanceCategories",
                columns: new[] { "WorkspaceId", "Kind" },
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCommands_ActorUserId",
                table: "CatalogCommands",
                column: "ActorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogCommands");

            migrationBuilder.DropIndex(
                name: "IX_FinanceCategories_WorkspaceId_Kind",
                table: "FinanceCategories");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "FinanceCategories");
        }
    }
}
