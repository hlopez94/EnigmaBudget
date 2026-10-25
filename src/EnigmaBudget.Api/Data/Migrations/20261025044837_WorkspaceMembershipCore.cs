using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceMembershipCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workspaces_OwnerSubject",
                table: "Workspaces");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerSubject",
                table: "Workspaces",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Workspaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Workspaces",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Personal");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Workspaces",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "Personal");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Workspaces",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Workspaces",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ApplicationUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceAuditEvents_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkspaceAuditEvents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMemberships",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMemberships", x => new { x.WorkspaceId, x.UserId });
                    table.ForeignKey(
                        name: "FK_WorkspaceMemberships_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkspaceMemberships_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_OwnerSubject",
                table: "Workspaces",
                column: "OwnerSubject",
                unique: true,
                filter: "\"OwnerSubject\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_OwnerUserId",
                table: "Workspaces",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_Subject",
                table: "ApplicationUsers",
                column: "Subject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceAuditEvents_ActorUserId",
                table: "WorkspaceAuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceAuditEvents_WorkspaceId_OccurredAt",
                table: "WorkspaceAuditEvents",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemberships_UserId_Active",
                table: "WorkspaceMemberships",
                columns: new[] { "UserId", "Active" });

            migrationBuilder.Sql("""
                INSERT INTO "ApplicationUsers" ("Id", "Subject", "EmailVerified", "CreatedAt")
                SELECT gen_random_uuid(), w."OwnerSubject", FALSE, CURRENT_TIMESTAMP
                FROM "Workspaces" AS w
                WHERE w."OwnerSubject" IS NOT NULL
                ON CONFLICT ("Subject") DO NOTHING;

                UPDATE "Workspaces" AS w
                SET "OwnerUserId" = u."Id", "Kind" = 'Personal', "Name" = 'Personal', "Version" = 1
                FROM "ApplicationUsers" AS u
                WHERE u."Subject" = w."OwnerSubject";

                INSERT INTO "WorkspaceMemberships" ("WorkspaceId", "UserId", "Role", "Active", "Version", "JoinedAt")
                SELECT w."Id", w."OwnerUserId", 'Owner', TRUE, 1, CURRENT_TIMESTAMP
                FROM "Workspaces" AS w
                ON CONFLICT ("WorkspaceId", "UserId") DO NOTHING;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "Workspaces",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Personal");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Workspaces",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldDefaultValue: "Personal");

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "Workspaces",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);


            migrationBuilder.AddForeignKey(
                name: "FK_Workspaces_ApplicationUsers_OwnerUserId",
                table: "Workspaces",
                column: "OwnerUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Workspaces_ApplicationUsers_OwnerUserId",
                table: "Workspaces");

            migrationBuilder.DropTable(
                name: "WorkspaceAuditEvents");

            migrationBuilder.DropTable(
                name: "WorkspaceMemberships");

            migrationBuilder.DropTable(
                name: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_Workspaces_OwnerSubject",
                table: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_Workspaces_OwnerUserId",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Workspaces");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerSubject",
                table: "Workspaces",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_OwnerSubject",
                table: "Workspaces",
                column: "OwnerSubject",
                unique: true);
        }
    }
}
