using EnigmaBudget.Persistence.Contexts.EfCore.Enigma;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace EnigmaBudget.WebApi.Configuration
{
    public static class WebAppConfigurationExtensions
    {

        public static void ConfigLogs(this WebApplicationBuilder builder)
        {
            var logFile = builder.Configuration.GetValue<string>("Logs:FileRoute");
        }
        public static void ConfigureCORS(this WebApplicationBuilder builder)
        {
            var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();
            builder.Services.AddCors(options =>
                options.AddPolicy(name: "enigmaapp",
                    policy =>
                    {
                        policy.WithOrigins(corsOrigins);
                    }
                ));
        }
        public static void ConfigDataBases(this WebApplicationBuilder builder)
        {
            MariaDBConfig MariaDbConfig = builder.Configuration.GetRequiredSection("MariaDB").Get<MariaDBConfig>()!;

            builder.Services.AddScoped(_ => new MySqlConnection(MariaDbConfig.ConnectionString));

            builder.Services.AddDbContext<EnigmaContext>(
                options => options.UseMySql(
                                    MariaDbConfig.ConnectionString,
                                    ServerVersion.AutoDetect(MariaDbConfig.ConnectionString),
                                    x => x.MigrationsAssembly($"EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Migrations.{MariaDbConfig.Environment}")
                            )
                );
        }
    }
}
