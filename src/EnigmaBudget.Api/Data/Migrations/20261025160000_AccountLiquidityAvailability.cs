using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations;

public partial class AccountLiquidityAvailability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(name: "FundsAvailableFrom", table: "Accounts", type: "date", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Reason", table: "SavingsMovements", type: "character varying(240)", maxLength: 240, nullable: true);
        migrationBuilder.AddCheckConstraint(name: "CK_SavingsMovements_Kind_Allowed", table: "SavingsMovements", sql: "\"Kind\" IN ('Allocate','Release','Apply','Reversal')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_SavingsMovements_Kind_Allowed", table: "SavingsMovements");
        migrationBuilder.DropColumn(name: "FundsAvailableFrom", table: "Accounts");
        migrationBuilder.DropColumn(name: "Reason", table: "SavingsMovements");
    }
}
