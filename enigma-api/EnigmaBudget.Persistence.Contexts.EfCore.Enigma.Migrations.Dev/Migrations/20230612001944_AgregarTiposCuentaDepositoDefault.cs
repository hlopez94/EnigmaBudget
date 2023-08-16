using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Migrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class AgregarTiposCuentaDepositoDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.InsertData(
                table: "types_deposit_account",
                columns: new[] { "tda_id", "tda_description", "tda_enum_name", "tda_fecha_alta", "tda_fecha_baja", "tda_fecha_modif", "tda_name" },
                values: new object[,]
                {
                    { 1L, "Caja de Ahorro", "CAJA_AHORRO", new DateOnly(2023, 6, 11), null, new DateOnly(2023, 6, 11), "Caja de Ahorro" },
                    { 2L, "Cuenta Corriente", "CUENTA_CORRIENTE", new DateOnly(2023, 6, 11), null, new DateOnly(2023, 6, 11), "Cuenta Corriente" },
                    { 3L, "Billetera Física", "BILLETERA_FISICA", new DateOnly(2023, 6, 11), null, new DateOnly(2023, 6, 11), "Billetera Física" },
                    { 4L, "Billetera Virtual", "BILLETERA_VIRTUAL", new DateOnly(2023, 6, 11), null, new DateOnly(2023, 6, 11), "Billetera Virtual" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 4L);

            migrationBuilder.AlterColumn<decimal>(
                name: "dea_funds",
                table: "deposit_accounts",
                type: "decimal(10,30)",
                precision: 10,
                nullable: false,
                comment: "Fondos actuales",
                oldClrType: typeof(decimal),
                oldType: "decimal(10)",
                oldPrecision: 10,
                oldComment: "Fondos actuales");

        }
    }
}
