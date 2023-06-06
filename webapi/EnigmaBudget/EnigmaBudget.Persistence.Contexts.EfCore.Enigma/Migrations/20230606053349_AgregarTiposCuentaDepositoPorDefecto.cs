using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Migrations
{
    /// <inheritdoc />
    public partial class AgregarTiposCuentaDepositoPorDefecto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "tda_name",
                table: "types_deposit_account",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                collation: "utf8mb4_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldMaxLength: 16)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.AlterColumn<decimal>(
                name: "dea_funds",
                table: "deposit_accounts",
                type: "decimal(10)",
                precision: 10,
                nullable: false,
                comment: "Fondos actuales",
                oldClrType: typeof(decimal),
                oldType: "decimal(10,30)",
                oldPrecision: 10,
                oldComment: "Fondos actuales"); 
            
            migrationBuilder.InsertData("types_deposit_account",
                new[] { "tda_name", "tda_description", "tda_enum_name", "tda_fecha_alta", "tda_fecha_modif" },
                new object[,] {
                    { "Caja de Ahorro", "Caja de Ahorro", "CAJA_AHORRO", DateTime.Now, DateTime.Now },
                    { "Cuenta Corriente", "Cuenta Corriente", "CUENTA_CORRIENTE", DateTime.Now, DateTime.Now },
                    { "Billetera Física", "Billetera Física", "BILLETERA_FISICA", DateTime.Now, DateTime.Now },
                    { "Billetera Virtual", "Billetera Virtual", "BILLETERA_VIRTUAL", DateTime.Now, DateTime.Now },
                }
                );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData("types_deposit_account",
              "tda_enum_name",
             new object[] {
                    "CAJA_AHORRO",
                    "CUENTA_CORRIENTE",
                    "BILLETERA_FISICA",
                    "BILLETERA_VIRTUAL"
             }
             );

            migrationBuilder.AlterColumn<string>(
                name: "tda_name",
                table: "types_deposit_account",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                collation: "utf8mb4_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_general_ci");

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
