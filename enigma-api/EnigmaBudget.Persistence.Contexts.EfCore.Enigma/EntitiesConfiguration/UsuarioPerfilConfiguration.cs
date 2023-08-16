using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.EntitiesConfiguration
{
    public class UsuarioPerfilConfiguration : IEntityTypeConfiguration<UsuarioPerfilEntity>
    {
        public void Configure(EntityTypeBuilder<UsuarioPerfilEntity> entity)
        {
            entity.HasKey(e => e.UspUsuId).HasName("PRIMARY");

            entity.ToTable("usuario_perfil");

            entity.Property(e => e.UspUsuId)
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint(20)")
                .HasColumnName("usp_usu_id");

            entity.Property(e => e.UspFechaAlta)
                .HasColumnType("datetime")
                .HasColumnName("usp_fecha_alta");

            entity.Property(e => e.UspFechaBaja)
                .HasColumnType("datetime")
                .HasColumnName("usp_fecha_baja")
                .HasDefaultValueSql("sysdate()");

            entity.Property(e => e.UspFechaModif)
                .HasColumnType("datetime")
                .HasColumnName("usp_fecha_modif")
                .HasDefaultValueSql("sysdate()");

            entity.Property(e => e.UspFechaNacimiento).HasColumnName("usp_fecha_nacimiento");

            entity.Property(e => e.UspNombre)
                .HasMaxLength(100)
                .HasColumnName("usp_nombre");

            entity.Property(e => e.UspTelCodArea)
                .HasColumnType("smallint(6)")
                .HasColumnName("usp_tel_cod_area");

            entity.Property(e => e.UspTelCodPais)
                .HasColumnType("smallint(6)")
                .HasColumnName("usp_tel_cod_pais");

            entity.Property(e => e.UspTelNro)
                .HasColumnType("int(11)")
                .HasColumnName("usp_tel_nro");

            entity.HasOne(d => d.UspUsu).WithOne(p => p.UsuarioPerfil)
                .HasForeignKey<UsuarioPerfilEntity>(d => d.UspUsuId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_usp_usu");

        }
    }
}
