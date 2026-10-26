using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceInvitationsAndPersonalReadGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActivationGeneration",
                table: "WorkspaceMemberships",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateTable(
                name: "PersonalReadGrants",
                columns: table => new
                {
                    PersonalWorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdWorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalReadGrants", x => new { x.PersonalWorkspaceId, x.HouseholdWorkspaceId });
                    table.ForeignKey(
                        name: "FK_PersonalReadGrants_Workspaces_HouseholdWorkspaceId",
                        column: x => x.HouseholdWorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonalReadGrants_Workspaces_PersonalWorkspaceId",
                        column: x => x.PersonalWorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedMembershipGeneration = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceInvitations_ApplicationUsers_AcceptedByUserId",
                        column: x => x.AcceptedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkspaceInvitations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonalReadGrantAudience",
                columns: table => new
                {
                    PersonalWorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdWorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipGeneration = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalReadGrantAudience", x => new { x.PersonalWorkspaceId, x.HouseholdWorkspaceId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PersonalReadGrantAudience_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonalReadGrantAudience_PersonalReadGrants_PersonalWorksp~",
                        columns: x => new { x.PersonalWorkspaceId, x.HouseholdWorkspaceId },
                        principalTable: "PersonalReadGrants",
                        principalColumns: new[] { "PersonalWorkspaceId", "HouseholdWorkspaceId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalReadGrantAudience_UserId",
                table: "PersonalReadGrantAudience",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalReadGrants_HouseholdWorkspaceId",
                table: "PersonalReadGrants",
                column: "HouseholdWorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvitations_AcceptedByUserId",
                table: "WorkspaceInvitations",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvitations_TokenHash",
                table: "WorkspaceInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvitations_WorkspaceId_Id",
                table: "WorkspaceInvitations",
                columns: new[] { "WorkspaceId", "Id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonalReadGrantAudience");

            migrationBuilder.DropTable(
                name: "WorkspaceInvitations");

            migrationBuilder.DropTable(
                name: "PersonalReadGrants");

            migrationBuilder.DropColumn(
                name: "ActivationGeneration",
                table: "WorkspaceMemberships");
        }
    }
}
