using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.EntitiesConfiguration
{
    public class DepositAccountTransactionConfiguration : IEntityTypeConfiguration<DepositAccountTransactionEntity>
    {
        public void Configure(EntityTypeBuilder<DepositAccountTransactionEntity> entity)
        {
            entity.HasKey(e => e.DatId).HasName("PRIMARY");

            entity.ToTable("deposit_account_transactions", tb => tb.HasComment("Tabla con datos de transacciones de cuentas depósito"));

            entity.HasIndex(e => e.DatUsuId, "FK_dat_usu");

            entity.HasIndex(e => e.DatDeaId, "FK_dat_dea");

            entity.HasIndex(e => e.DatFechaBaja, "IDX_dat_baja");

            entity.Property(e => e.DatId)
                .HasComment("ID Unico para transacción depósito")
                .HasColumnType("bigint(20)")
                .HasColumnName("dat_id");

            entity.Property(e => e.DatCurrencyCode)
                .HasMaxLength(3)
                .HasComment("Codigo numerico ISO-4217 para pais de cuenta depósito")
                .HasColumnName("dat_currency_code")
                .IsRequired();

            entity.Property(e => e.DatDescription)
                .HasMaxLength(100)
                .HasComment("Nombre descriptivo del movimiento")
                .HasColumnName("dat_description")
                .IsRequired();

            entity.Property(e => e.DatDetails)
                .HasMaxLength(3000)
                .HasComment("Detalles del movimiento")
                .HasColumnName("dat_details")
                ;

            entity.Property(e => e.DatFechaAlta)
                .HasComment("Fecha de alta del movimiento")
                .HasColumnType("datetime")
                .HasColumnName("dat_fecha_alta")
                .IsRequired()
                .ValueGeneratedOnAdd()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore); ;

            entity.Property(e => e.DatFechaBaja)
                .HasComment("Fecha de baja del movimiento")
                .HasColumnType("datetime")
                .HasColumnName("dat_fecha_baja")
                ;

            entity.Property(e => e.DatFechaModif)
                .HasComment("Fecha de modificación del movimiento")
                .HasColumnType("datetime")
                .HasColumnName("dat_fecha_modif")
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                ;

            entity.Property(e => e.DatTransactionDate)
                .HasComment("Fecha de realización del movimiento en cuenta")
                .HasColumnType("datetime")
                .HasColumnName("dat_fecha_transaccion")
                .IsRequired()
                ;

            entity.Property(e => e.DatAmmount)
                .HasPrecision(19, 4)
                .HasComment("Monto del Movimiento")
                .HasColumnName("dat_ammount");

            entity.Property(e => e.DatUsuId)
                .HasComment("ID usuario del movimiento")
                .HasColumnType("bigint(20)")
                .HasColumnName("dat_usu_id")
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

            entity.Property(e => e.DatDeaId)
                .HasComment("ID cuenta deposito del movimiento")
                .HasColumnType("bigint(20)")
                .HasColumnName("dat_dea_id")
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore); ;

            entity.HasOne(d => d.DatDea).WithMany(p => p.DeaTrd)
                .HasForeignKey(d => d.DatDeaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_dea_tda");

            entity.HasOne(d => d.DatUsu).WithMany(p => p.DepositAccountsTransactions)
                .HasForeignKey(d => d.DatUsuId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_dea_usu");

        }
    }
}
