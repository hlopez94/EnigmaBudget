using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.EntitiesConfiguration
{
    public class UsuarioValidacionEmailConfiguration : IEntityTypeConfiguration<UsuariosValidacionEmailEntity>
    {
        public void Configure(EntityTypeBuilder<UsuariosValidacionEmailEntity> entity)
        {
            entity.HasKey(e => e.UveId).HasName("PRIMARY");

            entity.ToTable("usuarios_validacion_email");

            entity.HasIndex(e => e.UveUsuId, "usuarios_validacion_email_FK");

            entity.HasIndex(e => e.UveId, "usuarios_validacion_email_uve_ID_IDX");

            entity.HasIndex(e => new { e.UveSalt, e.UveId }, "usuarios_validacion_email_uve_salt_IDX");

            entity.Property(e => e.UveId)
                .HasColumnType("int(11)")
                .HasColumnName("uve_id");
            entity.Property(e => e.UveFechaAlta)
                .HasColumnType("datetime")
                .HasColumnName("uve_fecha_alta");
            entity.Property(e => e.UveFechaBaja)
                .HasColumnType("datetime")
                .HasColumnName("uve_fecha_baja");
            entity.Property(e => e.UveNuevoCorreo)
                .HasMaxLength(100)
                .HasDefaultValueSql("'0'")
                .HasColumnName("uve_nuevo_correo");
            entity.Property(e => e.UveSalt)
                .HasMaxLength(64)
                .HasColumnName("uve_salt");
            entity.Property(e => e.UveUsuId)
                .HasColumnType("bigint(20)")
                .HasColumnName("uve_usu_id");
            entity.Property(e => e.UveValidado).HasColumnName("uve_validado");

            entity.HasOne(d => d.UveUsu).WithMany(p => p.UsuariosValidacionEmails)
                .HasForeignKey(d => d.UveUsuId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_usv_usu");

        }
    }
}
