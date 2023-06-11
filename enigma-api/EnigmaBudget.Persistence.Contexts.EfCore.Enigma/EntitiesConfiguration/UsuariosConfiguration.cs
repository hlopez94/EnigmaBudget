using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma.EntitiesConfiguration
{
    public class UsuariosConfiguration : IEntityTypeConfiguration<UsuarioEntity>
    {
        public void Configure(EntityTypeBuilder<UsuarioEntity> entity)
        {

            entity.HasKey(e => e.UsuId).HasName("PRIMARY");

            entity.ToTable("usuarios", tb => tb.HasComment("Tabla con datos de usuarios"));

            entity.HasIndex(e => e.UsuCorreo, "IDX_usuarios_usu_correo").IsUnique();

            entity.HasIndex(e => e.UsuUsuario, "IDX_usuarios_usu_usuario").IsUnique();

            entity.Property(e => e.UsuId)
                .HasComment("ID Unico para el usuario")
                .HasColumnType("bigint(20)")
                .HasColumnName("usu_id");
            entity.Property(e => e.UsuCorreo)
                .HasMaxLength(100)
                .HasComment("Correo asociado al Usuario")
                .HasColumnName("usu_correo");
            entity.Property(e => e.UsuCorreoValidado)
                .HasComment("Indica si el mail brindado por el usuario fue validado")
                .HasColumnName("usu_correo_validado");
            entity.Property(e => e.UsuFechaAlta)
                .HasComment("Fecha de alta del usuario")
                .HasColumnType("datetime")
                .HasColumnName("usu_fecha_alta");
            entity.Property(e => e.UsuFechaBaja)
                .HasComment("Fecha de baja del usuario")
                .HasColumnType("datetime")
                .HasColumnName("usu_fecha_baja");
            entity.Property(e => e.UsuFechaModif)
                .HasComment("Fecha de modificación del usuario")
                .HasColumnType("datetime")
                .HasColumnName("usu_fecha_modif");
            entity.Property(e => e.UsuPassword)
                .HasMaxLength(256)
                .HasComment("Clave hasheada del usuario")
                .HasColumnName("usu_password");
            entity.Property(e => e.UsuSeed)
                .HasMaxLength(64)
                .HasComment("Semilla de hasheo de la clave de usuario")
                .HasColumnName("usu_seed");
            entity.Property(e => e.UsuUsuario)
                .HasMaxLength(100)
                .HasComment("Nombre de usuario")
                .HasColumnName("usu_usuario");

        }
    }
}
