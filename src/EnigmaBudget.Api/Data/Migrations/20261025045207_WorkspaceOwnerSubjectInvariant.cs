using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceOwnerSubjectInvariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Workspaces_KindAndOwnerSubject",
                table: "Workspaces",
                sql: "\"Kind\" IN ('Personal','Household') AND ((\"Kind\" = 'Personal' AND \"OwnerSubject\" IS NOT NULL) OR (\"Kind\" <> 'Personal' AND \"OwnerSubject\" IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Workspaces_KindAndOwnerSubject",
                table: "Workspaces");
        }
    }
}
