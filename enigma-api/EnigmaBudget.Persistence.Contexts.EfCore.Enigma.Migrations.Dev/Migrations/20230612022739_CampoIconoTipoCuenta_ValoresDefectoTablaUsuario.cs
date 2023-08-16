using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Migrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class CampoIconoTipoCuenta_ValoresDefectoTablaUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "usp_fecha_modif",
                table: "usuario_perfil",
                type: "datetime",
                nullable: false,
                defaultValueSql: "sysdate()",
                oldClrType: typeof(DateTime),
                oldType: "datetime");

            migrationBuilder.AlterColumn<DateTime>(
                name: "usp_fecha_baja",
                table: "usuario_perfil",
                type: "datetime",
                nullable: true,
                defaultValueSql: "sysdate()",
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tda_icon",
                table: "types_deposit_account",
                type: "varchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 1L,
                column: "tda_icon",
                value: "savings");

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 2L,
                column: "tda_icon",
                value: "account_balance");

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 3L,
                column: "tda_icon",
                value: "wallet");

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 4L,
                column: "tda_icon",
                value: "contactless");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tda_icon",
                table: "types_deposit_account");

            migrationBuilder.AlterColumn<DateTime>(
                name: "usp_fecha_modif",
                table: "usuario_perfil",
                type: "datetime",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldDefaultValueSql: "sysdate()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "usp_fecha_baja",
                table: "usuario_perfil",
                type: "datetime",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldNullable: true,
                oldDefaultValueSql: "sysdate()");
        }
    }
}
