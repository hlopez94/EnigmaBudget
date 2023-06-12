using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Migrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class CorreccionPrecisionCuentasFunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "dea_funds",
                table: "deposit_accounts",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                comment: "Fondos actuales",
                oldClrType: typeof(decimal),
                oldType: "decimal(10,30)",
                oldPrecision: 10,
                oldComment: "Fondos actuales");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "dea_funds",
                table: "deposit_accounts",
                type: "decimal(10,30)",
                precision: 10,
                nullable: false,
                comment: "Fondos actuales",
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)",
                oldPrecision: 19,
                oldScale: 4,
                oldComment: "Fondos actuales");
        }
    }
}
