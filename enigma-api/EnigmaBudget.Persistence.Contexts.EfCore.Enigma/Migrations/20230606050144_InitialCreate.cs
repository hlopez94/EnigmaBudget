using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "types_deposit_account",
                columns: table => new
                {
                    tda_id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    tda_description = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tda_name = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tda_enum_name = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tda_fecha_alta = table.Column<DateOnly>(type: "date", nullable: false),
                    tda_fecha_modif = table.Column<DateOnly>(type: "date", nullable: false),
                    tda_fecha_baja = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.tda_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci")
                ;

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    usu_id = table.Column<long>(type: "bigint(20)", nullable: false, comment: "ID Unico para el usuario")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    usu_usuario = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, comment: "Nombre de usuario", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    usu_correo = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, comment: "Correo asociado al Usuario", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    usu_password = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, comment: "Clave hasheada del usuario", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    usu_seed = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, comment: "Semilla de hasheo de la clave de usuario", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    usu_fecha_alta = table.Column<DateTime>(type: "datetime", nullable: false, comment: "Fecha de alta del usuario"),
                    usu_fecha_modif = table.Column<DateTime>(type: "datetime", nullable: false, comment: "Fecha de modificación del usuario"),
                    usu_fecha_baja = table.Column<DateTime>(type: "datetime", nullable: true, comment: "Fecha de baja del usuario"),
                    usu_correo_validado = table.Column<bool>(type: "tinyint(1)", nullable: false, comment: "Indica si el mail brindado por el usuario fue validado")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.usu_id);
                },
                comment: "Tabla con datos de usuarios")
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "deposit_accounts",
                columns: table => new
                {
                    dea_id = table.Column<long>(type: "bigint(20)", nullable: false, comment: "ID Unico para cuenta depósito")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    dea_usu_id = table.Column<long>(type: "bigint(20)", nullable: false, comment: "ID usuario duenio cuenta depósito"),
                    dea_tda_id = table.Column<long>(type: "bigint(20)", nullable: false, comment: "Tipo cuenta depóito ID"),
                    dea_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, comment: "Nombre cuenta deposito", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dea_description = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, comment: "Nombre descriptivo cuenta depósito", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dea_funds = table.Column<decimal>(type: "decimal(10)", precision: 10, nullable: false, comment: "Fondos actuales"),
                    dea_country_code = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false, comment: "Codigo numerico ISO-4217 para moneda de cuenta depósito", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dea_currency_code = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false, comment: "Codigo numerico ISO-4217 para pais de cuenta depósito", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dea_fecha_alta = table.Column<DateTime>(type: "datetime", nullable: false, comment: "Fecha de alta de nta deposito"),
                    dea_fecha_modif = table.Column<DateTime>(type: "datetime", nullable: false, comment: "Fecha de modificación de cuenta deposito"),
                    dea_fecha_baja = table.Column<DateTime>(type: "datetime", nullable: true, comment: "Fecha de baja de cuenta depósito")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.dea_id);
                    table.ForeignKey(
                        name: "FK_depoist_accounts",
                        column: x => x.dea_usu_id,
                        principalTable: "usuarios",
                        principalColumn: "usu_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deposit_accounts",
                        column: x => x.dea_tda_id,
                        principalTable: "types_deposit_account",
                        principalColumn: "tda_id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Tabla con datos de cuentas depósito")
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "usuario_perfil",
                columns: table => new
                {
                    usp_usu_id = table.Column<long>(type: "bigint(20)", nullable: false),
                    usp_nombre = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    usp_fecha_nacimiento = table.Column<DateOnly>(type: "date", nullable: true),
                    usp_tel_cod_pais = table.Column<short>(type: "smallint(6)", nullable: true),
                    usp_tel_cod_area = table.Column<short>(type: "smallint(6)", nullable: true),
                    usp_tel_nro = table.Column<int>(type: "int(11)", nullable: true),
                    usp_fecha_alta = table.Column<DateTime>(type: "datetime", nullable: false),
                    usp_fecha_modif = table.Column<DateTime>(type: "datetime", nullable: false),
                    usp_fecha_baja = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.usp_usu_id);
                    table.ForeignKey(
                        name: "FK_usuario_perfil_usuarios",
                        column: x => x.usp_usu_id,
                        principalTable: "usuarios",
                        principalColumn: "usu_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "usuarios_validacion_email",
                columns: table => new
                {
                    uve_id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    uve_usu_id = table.Column<long>(type: "bigint(20)", nullable: false),
                    uve_fecha_alta = table.Column<DateTime>(type: "datetime", nullable: false),
                    uve_fecha_baja = table.Column<DateTime>(type: "datetime", nullable: false),
                    uve_salt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    uve_validado = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    uve_nuevo_correo = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, defaultValueSql: "'0'", collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.uve_id);
                    table.ForeignKey(
                        name: "usuarios_validacion_email_FK",
                        column: x => x.uve_usu_id,
                        principalTable: "usuarios",
                        principalColumn: "usu_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "FK_depoist_accounts",
                table: "deposit_accounts",
                column: "dea_usu_id");

            migrationBuilder.CreateIndex(
                name: "FK_deposit_accounts",
                table: "deposit_accounts",
                column: "dea_tda_id");

            migrationBuilder.CreateIndex(
                name: "IDX_dea_baja",
                table: "deposit_accounts",
                column: "dea_fecha_baja");

            migrationBuilder.CreateIndex(
                name: "IDX_types_deposit_account_baja",
                table: "types_deposit_account",
                column: "tda_fecha_baja");

            migrationBuilder.CreateIndex(
                name: "IDX_types_deposit_account_enum",
                table: "types_deposit_account",
                column: "tda_enum_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IDX_usuarios_usu_correo",
                table: "usuarios",
                column: "usu_correo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IDX_usuarios_usu_usuario",
                table: "usuarios",
                column: "usu_usuario",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "usuarios_validacion_email_FK",
                table: "usuarios_validacion_email",
                column: "uve_usu_id");

            migrationBuilder.CreateIndex(
                name: "usuarios_validacion_email_uve_ID_IDX",
                table: "usuarios_validacion_email",
                column: "uve_id");

            migrationBuilder.CreateIndex(
                name: "usuarios_validacion_email_uve_salt_IDX",
                table: "usuarios_validacion_email",
                columns: new[] { "uve_salt", "uve_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deposit_accounts");

            migrationBuilder.DropTable(
                name: "usuario_perfil");

            migrationBuilder.DropTable(
                name: "usuarios_validacion_email");

            migrationBuilder.DropTable(
                name: "types_deposit_account");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
