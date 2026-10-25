using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceInvitationAuthorityGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IssuedForUserId",
                table: "WorkspaceInvitations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IssuedForMembershipVersion",
                table: "WorkspaceInvitations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvitations_IssuedForUserId",
                table: "WorkspaceInvitations",
                column: "IssuedForUserId");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkspaceInvitations_IssuedForUserId",
                table: "WorkspaceInvitations");

            migrationBuilder.DropColumn(
                name: "IssuedForUserId",
                table: "WorkspaceInvitations");

            migrationBuilder.DropColumn(
                name: "IssuedForMembershipVersion",
                table: "WorkspaceInvitations");
        }
    }
}
