using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinanceCatalogSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CatalogSelectionHash",
                table: "Purchases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MerchantNameSnapshot",
                table: "Purchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatalogSelectionHash",
                table: "FinanceOperations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncomeSourceNameSnapshot",
                table: "FinanceOperations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MerchantNameSnapshot",
                table: "FinanceOperations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CatalogSelectionHash",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "MerchantNameSnapshot",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "CatalogSelectionHash",
                table: "FinanceOperations");

            migrationBuilder.DropColumn(
                name: "IncomeSourceNameSnapshot",
                table: "FinanceOperations");

            migrationBuilder.DropColumn(
                name: "MerchantNameSnapshot",
                table: "FinanceOperations");
        }
    }
}
