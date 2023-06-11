using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, true)
                .AddUserSecrets(Assembly.Load("EnigmaBudget.Persistence.Contexts.EfCore.Enigma"), true)
                .Build();

            optionsBuilder.UseMySql(configuration.GetConnectionString("EnigmaDb"), ServerVersion.AutoDetect(configuration.GetConnectionString("EnigmaDb")));
        }

    }

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
