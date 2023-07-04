using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Migrations.Dev.Migrations
{
    /// <inheritdoc />
    public partial class AgregarTransacciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_depoist_accounts",
                table: "deposit_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_deposit_accounts",
                table: "deposit_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_usuario_perfil_usuarios",
                table: "usuario_perfil");

            migrationBuilder.DropForeignKey(
                name: "usuarios_validacion_email_FK",
                table: "usuarios_validacion_email");

            migrationBuilder.RenameIndex(
                name: "FK_deposit_accounts",
                table: "deposit_accounts",
                newName: "FK_dea_tda");

            migrationBuilder.RenameIndex(
                name: "FK_depoist_accounts",
                table: "deposit_accounts",
                newName: "FK_dea_usu");

            migrationBuilder.CreateTable(
                name: "deposit_account_transactions",
                columns: table => new
                {
                    dat_id = table.Column<long>(type: "bigint(20)", nullable: false, comment: "ID Unico para transacción depósito")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    dat_description = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, comment: "Nombre descriptivo del movimiento", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dat_details = table.Column<string>(type: "varchar(3000)", maxLength: 3000, nullable: false, comment: "Detalles del movimiento", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dat_ammount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, comment: "Monto del Movimiento"),
                    dat_fecha_transaccion = table.Column<DateTime>(type: "datetime", nullable: false, comment: "Fecha de realización del movimiento en cuenta"),
                    dat_fecha_alta = table.Column<DateTime>(type: "datetime", nullable: false, comment: "Fecha de alta del movimiento")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    dat_fecha_baja = table.Column<DateTime>(type: "datetime", nullable: true, comment: "Fecha de baja del movimiento"),
                    dat_fecha_modif = table.Column<DateTime>(type: "datetime", nullable: false, comment: "Fecha de modificación del movimiento")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn),
                    dat_currency_code = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false, comment: "Codigo numerico ISO-4217 para pais de cuenta depósito", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dat_dea_id = table.Column<long>(type: "bigint(20)", nullable: false, comment: "ID cuenta deposito del movimiento"),
                    dat_usu_id = table.Column<long>(type: "bigint(20)", nullable: false, comment: "ID usuario del movimiento")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.dat_id);
                    table.ForeignKey(
                        name: "FK_dea_tda",
                        column: x => x.dat_dea_id,
                        principalTable: "deposit_accounts",
                        principalColumn: "dea_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dea_usu",
                        column: x => x.dat_usu_id,
                        principalTable: "usuarios",
                        principalColumn: "usu_id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Tabla con datos de transacciones de cuentas depósito")
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 1L,
                columns: new[] { "tda_fecha_alta", "tda_fecha_modif" },
                values: new object[] { new DateOnly(2023, 7, 4), new DateOnly(2023, 7, 4) });

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 2L,
                columns: new[] { "tda_fecha_alta", "tda_fecha_modif" },
                values: new object[] { new DateOnly(2023, 7, 4), new DateOnly(2023, 7, 4) });

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 3L,
                columns: new[] { "tda_fecha_alta", "tda_fecha_modif" },
                values: new object[] { new DateOnly(2023, 7, 4), new DateOnly(2023, 7, 4) });

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 4L,
                columns: new[] { "tda_fecha_alta", "tda_fecha_modif" },
                values: new object[] { new DateOnly(2023, 7, 4), new DateOnly(2023, 7, 4) });

            migrationBuilder.CreateIndex(
                name: "FK_dat_dea",
                table: "deposit_account_transactions",
                column: "dat_dea_id");

            migrationBuilder.CreateIndex(
                name: "FK_dat_usu",
                table: "deposit_account_transactions",
                column: "dat_usu_id");

            migrationBuilder.CreateIndex(
                name: "IDX_dat_baja",
                table: "deposit_account_transactions",
                column: "dat_fecha_baja");

            migrationBuilder.AddForeignKey(
                name: "FK_deposit_accounts_types_deposit_account_dea_tda_id",
                table: "deposit_accounts",
                column: "dea_tda_id",
                principalTable: "types_deposit_account",
                principalColumn: "tda_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_deposit_accounts_usuarios_dea_usu_id",
                table: "deposit_accounts",
                column: "dea_usu_id",
                principalTable: "usuarios",
                principalColumn: "usu_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_usp_usu",
                table: "usuario_perfil",
                column: "usp_usu_id",
                principalTable: "usuarios",
                principalColumn: "usu_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_usv_usu",
                table: "usuarios_validacion_email",
                column: "uve_usu_id",
                principalTable: "usuarios",
                principalColumn: "usu_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_deposit_accounts_types_deposit_account_dea_tda_id",
                table: "deposit_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_deposit_accounts_usuarios_dea_usu_id",
                table: "deposit_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_usp_usu",
                table: "usuario_perfil");

            migrationBuilder.DropForeignKey(
                name: "FK_usv_usu",
                table: "usuarios_validacion_email");

            migrationBuilder.DropTable(
                name: "deposit_account_transactions");

            migrationBuilder.RenameIndex(
                name: "FK_dea_usu",
                table: "deposit_accounts",
                newName: "FK_depoist_accounts");

            migrationBuilder.RenameIndex(
                name: "FK_dea_tda",
                table: "deposit_accounts",
                newName: "FK_deposit_accounts");

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 1L,
                columns: new[] { "tda_fecha_alta", "tda_fecha_modif" },
                values: new object[] { new DateOnly(2023, 6, 11), new DateOnly(2023, 6, 11) });

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 2L,
                columns: new[] { "tda_fecha_alta", "tda_fecha_modif" },
                values: new object[] { new DateOnly(2023, 6, 11), new DateOnly(2023, 6, 11) });

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 3L,
                columns: new[] { "tda_fecha_alta", "tda_fecha_modif" },
                values: new object[] { new DateOnly(2023, 6, 11), new DateOnly(2023, 6, 11) });

            migrationBuilder.UpdateData(
                table: "types_deposit_account",
                keyColumn: "tda_id",
                keyValue: 4L,
                columns: new[] { "tda_fecha_alta", "tda_fecha_modif" },
                values: new object[] { new DateOnly(2023, 6, 11), new DateOnly(2023, 6, 11) });

            migrationBuilder.AddForeignKey(
                name: "FK_depoist_accounts",
                table: "deposit_accounts",
                column: "dea_usu_id",
                principalTable: "usuarios",
                principalColumn: "usu_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_deposit_accounts",
                table: "deposit_accounts",
                column: "dea_tda_id",
                principalTable: "types_deposit_account",
                principalColumn: "tda_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_usuario_perfil_usuarios",
                table: "usuario_perfil",
                column: "usp_usu_id",
                principalTable: "usuarios",
                principalColumn: "usu_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "usuarios_validacion_email_FK",
                table: "usuarios_validacion_email",
                column: "uve_usu_id",
                principalTable: "usuarios",
                principalColumn: "usu_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
