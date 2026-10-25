using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations;

public partial class ProjectionAssumptionSourceIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "SourceKind", table: "ProjectionAssumptions", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ComponentId", table: "ProjectionAssumptions", type: "character varying(160)", maxLength: 160, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SourceKind", table: "ProjectionAssumptions");
        migrationBuilder.DropColumn(name: "ComponentId", table: "ProjectionAssumptions");
    }
}
