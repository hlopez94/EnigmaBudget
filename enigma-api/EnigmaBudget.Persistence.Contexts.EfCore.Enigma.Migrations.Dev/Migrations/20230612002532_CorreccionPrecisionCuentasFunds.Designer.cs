﻿// <auto-generated />
using System;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Migrations.Dev.Migrations
{
    [DbContext(typeof(EnigmaContext))]
    [Migration("20230612002532_CorreccionPrecisionCuentasFunds")]
    partial class CorreccionPrecisionCuentasFunds
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .UseCollation("utf8mb4_general_ci")
                .HasAnnotation("ProductVersion", "7.0.5")
                .HasAnnotation("Relational:MaxIdentifierLength", 64);

            MySqlModelBuilderExtensions.HasCharSet(modelBuilder, "utf8mb4");

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.DepositAccountEntity", b =>
                {
                    b.Property<long>("DeaId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint(20)")
                        .HasColumnName("dea_id")
                        .HasComment("ID Unico para cuenta depósito");

                    b.Property<string>("DeaCountryCode")
                        .IsRequired()
                        .HasMaxLength(3)
                        .HasColumnType("varchar(3)")
                        .HasColumnName("dea_country_code")
                        .HasComment("Codigo numerico ISO-4217 para moneda de cuenta depósito");

                    b.Property<string>("DeaCurrencyCode")
                        .IsRequired()
                        .HasMaxLength(3)
                        .HasColumnType("varchar(3)")
                        .HasColumnName("dea_currency_code")
                        .HasComment("Codigo numerico ISO-4217 para pais de cuenta depósito");

                    b.Property<string>("DeaDescription")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("varchar(100)")
                        .HasColumnName("dea_description")
                        .HasComment("Nombre descriptivo cuenta depósito");

                    b.Property<DateTime>("DeaFechaAlta")
                        .HasColumnType("datetime")
                        .HasColumnName("dea_fecha_alta")
                        .HasComment("Fecha de alta de nta deposito");

                    b.Property<DateTime?>("DeaFechaBaja")
                        .HasColumnType("datetime")
                        .HasColumnName("dea_fecha_baja")
                        .HasComment("Fecha de baja de cuenta depósito");

                    b.Property<DateTime>("DeaFechaModif")
                        .HasColumnType("datetime")
                        .HasColumnName("dea_fecha_modif")
                        .HasComment("Fecha de modificación de cuenta deposito");

                    b.Property<decimal>("DeaFunds")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)")
                        .HasColumnName("dea_funds")
                        .HasComment("Fondos actuales");

                    b.Property<string>("DeaName")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("varchar(100)")
                        .HasColumnName("dea_name")
                        .HasComment("Nombre cuenta deposito");

                    b.Property<long>("DeaTdaId")
                        .HasColumnType("bigint(20)")
                        .HasColumnName("dea_tda_id")
                        .HasComment("Tipo cuenta depóito ID");

                    b.Property<long>("DeaUsuId")
                        .HasColumnType("bigint(20)")
                        .HasColumnName("dea_usu_id")
                        .HasComment("ID usuario duenio cuenta depósito");

                    b.HasKey("DeaId")
                        .HasName("PRIMARY");

                    b.HasIndex(new[] { "DeaUsuId" }, "FK_depoist_accounts");

                    b.HasIndex(new[] { "DeaTdaId" }, "FK_deposit_accounts");

                    b.HasIndex(new[] { "DeaFechaBaja" }, "IDX_dea_baja");

                    b.ToTable("deposit_accounts", null, t =>
                        {
                            t.HasComment("Tabla con datos de cuentas depósito");
                        });
                });

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.TypesDepositAccountEntity", b =>
                {
                    b.Property<long>("TdaId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint(20)")
                        .HasColumnName("tda_id");

                    b.Property<string>("TdaDescription")
                        .IsRequired()
                        .HasMaxLength(128)
                        .HasColumnType("varchar(128)")
                        .HasColumnName("tda_description");

                    b.Property<string>("TdaEnumName")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("varchar(32)")
                        .HasColumnName("tda_enum_name");

                    b.Property<DateOnly>("TdaFechaAlta")
                        .HasColumnType("date")
                        .HasColumnName("tda_fecha_alta");

                    b.Property<DateOnly?>("TdaFechaBaja")
                        .HasColumnType("date")
                        .HasColumnName("tda_fecha_baja");

                    b.Property<DateOnly>("TdaFechaModif")
                        .HasColumnType("date")
                        .HasColumnName("tda_fecha_modif");

                    b.Property<string>("TdaName")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("varchar(32)")
                        .HasColumnName("tda_name");

                    b.HasKey("TdaId")
                        .HasName("PRIMARY");

                    b.HasIndex(new[] { "TdaFechaBaja" }, "IDX_types_deposit_account_baja");

                    b.HasIndex(new[] { "TdaEnumName" }, "IDX_types_deposit_account_enum")
                        .IsUnique();

                    b.ToTable("types_deposit_account", (string)null);

                    b.HasData(
                        new
                        {
                            TdaId = 1L,
                            TdaDescription = "Caja de Ahorro",
                            TdaEnumName = "CAJA_AHORRO",
                            TdaFechaAlta = new DateOnly(2023, 6, 11),
                            TdaFechaModif = new DateOnly(2023, 6, 11),
                            TdaName = "Caja de Ahorro"
                        },
                        new
                        {
                            TdaId = 2L,
                            TdaDescription = "Cuenta Corriente",
                            TdaEnumName = "CUENTA_CORRIENTE",
                            TdaFechaAlta = new DateOnly(2023, 6, 11),
                            TdaFechaModif = new DateOnly(2023, 6, 11),
                            TdaName = "Cuenta Corriente"
                        },
                        new
                        {
                            TdaId = 3L,
                            TdaDescription = "Billetera Física",
                            TdaEnumName = "BILLETERA_FISICA",
                            TdaFechaAlta = new DateOnly(2023, 6, 11),
                            TdaFechaModif = new DateOnly(2023, 6, 11),
                            TdaName = "Billetera Física"
                        },
                        new
                        {
                            TdaId = 4L,
                            TdaDescription = "Billetera Virtual",
                            TdaEnumName = "BILLETERA_VIRTUAL",
                            TdaFechaAlta = new DateOnly(2023, 6, 11),
                            TdaFechaModif = new DateOnly(2023, 6, 11),
                            TdaName = "Billetera Virtual"
                        });
                });

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuarioEntity", b =>
                {
                    b.Property<long>("UsuId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint(20)")
                        .HasColumnName("usu_id")
                        .HasComment("ID Unico para el usuario");

                    b.Property<string>("UsuCorreo")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("varchar(100)")
                        .HasColumnName("usu_correo")
                        .HasComment("Correo asociado al Usuario");

                    b.Property<bool>("UsuCorreoValidado")
                        .HasColumnType("tinyint(1)")
                        .HasColumnName("usu_correo_validado")
                        .HasComment("Indica si el mail brindado por el usuario fue validado");

                    b.Property<DateTime>("UsuFechaAlta")
                        .HasColumnType("datetime")
                        .HasColumnName("usu_fecha_alta")
                        .HasComment("Fecha de alta del usuario");

                    b.Property<DateTime?>("UsuFechaBaja")
                        .HasColumnType("datetime")
                        .HasColumnName("usu_fecha_baja")
                        .HasComment("Fecha de baja del usuario");

                    b.Property<DateTime>("UsuFechaModif")
                        .HasColumnType("datetime")
                        .HasColumnName("usu_fecha_modif")
                        .HasComment("Fecha de modificación del usuario");

                    b.Property<string>("UsuPassword")
                        .HasMaxLength(256)
                        .HasColumnType("varchar(256)")
                        .HasColumnName("usu_password")
                        .HasComment("Clave hasheada del usuario");

                    b.Property<string>("UsuSeed")
                        .HasMaxLength(64)
                        .HasColumnType("varchar(64)")
                        .HasColumnName("usu_seed")
                        .HasComment("Semilla de hasheo de la clave de usuario");

                    b.Property<string>("UsuUsuario")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("varchar(100)")
                        .HasColumnName("usu_usuario")
                        .HasComment("Nombre de usuario");

                    b.HasKey("UsuId")
                        .HasName("PRIMARY");

                    b.HasIndex(new[] { "UsuCorreo" }, "IDX_usuarios_usu_correo")
                        .IsUnique();

                    b.HasIndex(new[] { "UsuUsuario" }, "IDX_usuarios_usu_usuario")
                        .IsUnique();

                    b.ToTable("usuarios", null, t =>
                        {
                            t.HasComment("Tabla con datos de usuarios");
                        });
                });

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuarioPerfilEntity", b =>
                {
                    b.Property<long>("UspUsuId")
                        .HasColumnType("bigint(20)")
                        .HasColumnName("usp_usu_id");

                    b.Property<DateTime>("UspFechaAlta")
                        .HasColumnType("datetime")
                        .HasColumnName("usp_fecha_alta");

                    b.Property<DateTime?>("UspFechaBaja")
                        .HasColumnType("datetime")
                        .HasColumnName("usp_fecha_baja");

                    b.Property<DateTime>("UspFechaModif")
                        .HasColumnType("datetime")
                        .HasColumnName("usp_fecha_modif");

                    b.Property<DateOnly?>("UspFechaNacimiento")
                        .HasColumnType("date")
                        .HasColumnName("usp_fecha_nacimiento");

                    b.Property<string>("UspNombre")
                        .HasMaxLength(100)
                        .HasColumnType("varchar(100)")
                        .HasColumnName("usp_nombre");

                    b.Property<short?>("UspTelCodArea")
                        .HasColumnType("smallint(6)")
                        .HasColumnName("usp_tel_cod_area");

                    b.Property<short?>("UspTelCodPais")
                        .HasColumnType("smallint(6)")
                        .HasColumnName("usp_tel_cod_pais");

                    b.Property<int?>("UspTelNro")
                        .HasColumnType("int(11)")
                        .HasColumnName("usp_tel_nro");

                    b.HasKey("UspUsuId")
                        .HasName("PRIMARY");

                    b.ToTable("usuario_perfil", (string)null);
                });

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuariosValidacionEmailEntity", b =>
                {
                    b.Property<int>("UveId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int(11)")
                        .HasColumnName("uve_id");

                    b.Property<DateTime>("UveFechaAlta")
                        .HasColumnType("datetime")
                        .HasColumnName("uve_fecha_alta");

                    b.Property<DateTime>("UveFechaBaja")
                        .HasColumnType("datetime")
                        .HasColumnName("uve_fecha_baja");

                    b.Property<string>("UveNuevoCorreo")
                        .IsRequired()
                        .ValueGeneratedOnAdd()
                        .HasMaxLength(100)
                        .HasColumnType("varchar(100)")
                        .HasColumnName("uve_nuevo_correo")
                        .HasDefaultValueSql("'0'");

                    b.Property<string>("UveSalt")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("varchar(64)")
                        .HasColumnName("uve_salt");

                    b.Property<long>("UveUsuId")
                        .HasColumnType("bigint(20)")
                        .HasColumnName("uve_usu_id");

                    b.Property<bool>("UveValidado")
                        .HasColumnType("tinyint(1)")
                        .HasColumnName("uve_validado");

                    b.HasKey("UveId")
                        .HasName("PRIMARY");

                    b.HasIndex(new[] { "UveUsuId" }, "usuarios_validacion_email_FK");

                    b.HasIndex(new[] { "UveId" }, "usuarios_validacion_email_uve_ID_IDX");

                    b.HasIndex(new[] { "UveSalt", "UveId" }, "usuarios_validacion_email_uve_salt_IDX");

                    b.ToTable("usuarios_validacion_email", (string)null);
                });

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.DepositAccountEntity", b =>
                {
                    b.HasOne("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.TypesDepositAccountEntity", "DeaTda")
                        .WithMany("DepositAccounts")
                        .HasForeignKey("DeaTdaId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("FK_deposit_accounts");

                    b.HasOne("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuarioEntity", "DeaUsu")
                        .WithMany("DepositAccounts")
                        .HasForeignKey("DeaUsuId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("FK_depoist_accounts");

                    b.Navigation("DeaTda");

                    b.Navigation("DeaUsu");
                });

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuarioPerfilEntity", b =>
                {
                    b.HasOne("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuarioEntity", "UspUsu")
                        .WithOne("UsuarioPerfil")
                        .HasForeignKey("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuarioPerfilEntity", "UspUsuId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("FK_usuario_perfil_usuarios");

                    b.Navigation("UspUsu");
                });

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuariosValidacionEmailEntity", b =>
                {
                    b.HasOne("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuarioEntity", "UveUsu")
                        .WithMany("UsuariosValidacionEmails")
                        .HasForeignKey("UveUsuId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("usuarios_validacion_email_FK");

                    b.Navigation("UveUsu");
                });

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.TypesDepositAccountEntity", b =>
                {
                    b.Navigation("DepositAccounts");
                });

            modelBuilder.Entity("EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities.UsuarioEntity", b =>
                {
                    b.Navigation("DepositAccounts");

                    b.Navigation("UsuarioPerfil");

                    b.Navigation("UsuariosValidacionEmails");
                });
#pragma warning restore 612, 618
        }
    }
}
