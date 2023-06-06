using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace EnigmaBudget.Persistence.Contexts.EfCore.Enigma;

public partial class EnigmaContext : DbContext
{
    public EnigmaContext()
    {
    }

    public EnigmaContext(DbContextOptions<EnigmaContext> options)
        : base(options)
    {
    }

    public virtual DbSet<DepositAccountEntity> DepositAccounts { get; set; }

    public virtual DbSet<TypesDepositAccountEntity> TypesDepositAccounts { get; set; }

    public virtual DbSet<UsuarioEntity> Usuarios { get; set; }

    public virtual DbSet<UsuarioPerfilEntity> UsuarioPerfiles { get; set; }

    public virtual DbSet<UsuariosValidacionEmailEntity> UsuariosValidacionEmails { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseMySql("name=ConnectionStrings:EnigmaDb", ServerVersion.Parse("10.5.9-mariadb"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_general_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
