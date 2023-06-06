using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.EntitiesConfiguration
{
    public class TypesDepositAccountConfiguration : IEntityTypeConfiguration<TypesDepositAccountEntity>
    {
        public void Configure(EntityTypeBuilder<TypesDepositAccountEntity> entity)
        {
            entity.HasKey(e => e.TdaId).HasName("PRIMARY");

            entity.ToTable("types_deposit_account");

            entity.HasIndex(e => e.TdaFechaBaja, "IDX_types_deposit_account_baja");

            entity.HasIndex(e => e.TdaEnumName, "IDX_types_deposit_account_enum").IsUnique();

            entity.Property(e => e.TdaId)
                .HasColumnType("bigint(20)")
                .HasColumnName("tda_id");
            entity.Property(e => e.TdaDescription)
                .HasMaxLength(128)
                .HasColumnName("tda_description");
            entity.Property(e => e.TdaEnumName)
                .HasMaxLength(32)
                .HasColumnName("tda_enum_name");
            entity.Property(e => e.TdaFechaAlta).HasColumnName("tda_fecha_alta");
            entity.Property(e => e.TdaFechaBaja).HasColumnName("tda_fecha_baja");
            entity.Property(e => e.TdaFechaModif).HasColumnName("tda_fecha_modif");
            entity.Property(e => e.TdaName)
                .HasMaxLength(16)
                .HasColumnName("tda_name");

        }
    }
}
