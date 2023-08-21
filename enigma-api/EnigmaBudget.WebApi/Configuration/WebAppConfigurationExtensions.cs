using EnigmaBudget.Persistence.Contexts.EfCore.Enigma;
using EnigmaBudget.WebApi.Filters;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace EnigmaBudget.WebApi.Configuration
{
    public static class WebAppConfigurationExtensions
    {
        public static void ConfigureLogs(this WebApplicationBuilder builder)
        {
        }

        public static void ConfigureCORS(this WebApplicationBuilder builder)
        {
            var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string>().Split(',');
            if(corsOrigins.Length == 0)
                throw new ArgumentNullException("Undefined CORS Origins");

            Console.Write(corsOrigins[0]);
            builder.Services.AddCors(options =>
                options.AddPolicy("enigmaapp", policy => policy.WithOrigins(corsOrigins)));
        }

        public static void ConfigureDataBases(this WebApplicationBuilder builder)
        {
            var connectionString = builder.Configuration.GetConnectionString("MariaDb");
            var env = builder.Configuration.GetValue<string>("Environment");

            builder.Services.AddScoped(_ => new MySqlConnection(connectionString));
            builder.Services.AddDbContext<EnigmaContext>(
                options => options.UseMySql(
                    connectionString,
                    ServerVersion.AutoDetect(connectionString),
                    x => x.MigrationsAssembly($"EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Migrations.{env}")
                )
                .LogTo(Console.WriteLine, LogLevel.Warning)
                .EnableDetailedErrors()
            );

            builder.Services.AddTransient<IStartupFilter, MigrationStartupFilter<EnigmaContext>>();
        }
    }
}
