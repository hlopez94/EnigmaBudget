using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancePaymentCommandPayloadHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommandPayloadHash",
                table: "FinancePayments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommandPayloadHash",
                table: "FinancePayments");
        }
    }
}
