using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.EntitiesConfiguration
{
    public class DepositAccountConfiguration : IEntityTypeConfiguration<DepositAccountEntity>
    {
        public void Configure(EntityTypeBuilder<DepositAccountEntity> entity)
        {
            entity.HasKey(e => e.DeaId).HasName("PRIMARY");

            entity.ToTable("deposit_accounts", tb => tb.HasComment("Tabla con datos de cuentas depósito"));

            entity.HasIndex(e => e.DeaUsuId, "FK_dea_usu");

            entity.HasIndex(e => e.DeaTdaId, "FK_dea_tda");

            entity.HasIndex(e => e.DeaFechaBaja, "IDX_dea_baja");

            entity.Property(e => e.DeaId)
                .HasComment("ID Unico para cuenta depósito")
                .HasColumnType("bigint(20)")
                .HasColumnName("dea_id");

            entity.Property(e => e.DeaCountryCode)
                .HasMaxLength(3)
                .HasComment("Codigo numerico ISO-4217 para moneda de cuenta depósito")
                .HasColumnName("dea_country_code");

            entity.Property(e => e.DeaCurrencyCode)
                .HasMaxLength(3)
                .HasComment("Codigo numerico ISO-4217 para pais de cuenta depósito")
                .HasColumnName("dea_currency_code");

            entity.Property(e => e.DeaDescription)
                .HasMaxLength(100)
                .HasComment("Nombre descriptivo cuenta depósito")
                .HasColumnName("dea_description");

            entity.Property(e => e.DeaFechaAlta)
                .HasComment("Fecha de alta de nta deposito")
                .HasColumnType("datetime")
                .HasColumnName("dea_fecha_alta");

            entity.Property(e => e.DeaFechaBaja)
                .HasComment("Fecha de baja de cuenta depósito")
                .HasColumnType("datetime")
                .HasColumnName("dea_fecha_baja");

            entity.Property(e => e.DeaFechaModif)
                .HasComment("Fecha de modificación de cuenta deposito")
                .HasColumnType("datetime")
                .HasColumnName("dea_fecha_modif");

            entity.Property(e => e.DeaFunds)
                .HasPrecision(19, 4)
                .HasComment("Fondos actuales")
                .HasColumnName("dea_funds");

            entity.Property(e => e.DeaName)
                .HasMaxLength(100)
                .HasComment("Nombre cuenta deposito")
                .HasColumnName("dea_name");

            entity.Property(e => e.DeaTdaId)
                .HasComment("Tipo cuenta depóito ID")
                .HasColumnType("bigint(20)")
                .HasColumnName("dea_tda_id");

            entity.Property(e => e.DeaUsuId)
                .HasComment("ID usuario duenio cuenta depósito")
                .HasColumnType("bigint(20)")
                .HasColumnName("dea_usu_id");

            entity.HasOne(d => d.DeaTda).WithMany(p => p.DepositAccounts)
                .HasForeignKey(d => d.DeaTdaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_deposit_accounts");

            entity.HasOne(d => d.DeaUsu).WithMany(p => p.DepositAccounts)
                .HasForeignKey(d => d.DeaUsuId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_depoist_accounts");

        }
    }
}
