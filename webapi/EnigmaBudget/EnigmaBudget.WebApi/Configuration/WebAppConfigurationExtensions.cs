using MySqlConnector;
using System.Data.Common;

namespace EnigmaBudget.WebApi.Configuration
{
    public static class WebAppConfigurationExtensions
    {

        public static void ConfigLogs(this WebApplicationBuilder builder)
        {
            var configuration = builder.Configuration;

            var logFile = builder.Configuration.GetValue<string>("Logs:FileRoute");

            builder.Services.AddScoped(_ => new MySqlConnection(builder.Configuration["MariaDB:ConnectionString"]));

            var corsOrigins = builder.Configuration.GetValue<string>("Cors:Origins");
            builder.Services.AddCors(options =>
                options.AddPolicy(name: "enigmaapp",
                    policy =>
                    {
                        policy.WithOrigins(corsOrigins);
                    }
                ));

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
        public static void RegisterConnectionStrings(this WebApplicationBuilder builder)
        {

            MariaDBConfig MariaDbConfig = builder.Configuration.GetSection("MariaDB").Get<MariaDBConfig>();

            builder.Services.AddScoped(_ => new MySqlConnection(MariaDbConfig.ConnectionString));


            var logFile = builder.Configuration.GetValue<string>("Logs:FileRoute");


        }
    }
}
