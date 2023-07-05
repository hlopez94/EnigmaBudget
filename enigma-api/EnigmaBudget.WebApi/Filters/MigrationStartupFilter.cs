using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace EnigmaBudget.WebApi.Filters
{
    public class MigrationStartupFilter<TContext> : IStartupFilter where TContext : DbContext
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                using(var scope = app.ApplicationServices.CreateScope())
                {
                    foreach(var context in scope.ServiceProvider.GetServices<TContext>())
                    {
                        // Create a CancellationTokenSource
                        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

                        // Get the CancellationToken from the CancellationTokenSource
                        CancellationToken cancellationToken = cancellationTokenSource.Token;
                        WaitForDatabase(cancellationToken, context.Database.GetConnectionString()).GetAwaiter().GetResult();
                        context.Database.SetCommandTimeout(160);
                        context.Database.Migrate();
                    }
                }
                next(app);
            };
        }
        private async Task WaitForDatabase(CancellationToken cancellationToken, string connectionString)
        {
            const int maxRetries = 10;
            const int retryDelaySeconds = 10;

            var retries = 0;
            var connected = false;

            while(!connected && retries < maxRetries)
            {
                try
                {
                    using(var connection = new MySqlConnection(connectionString))
                    {
                        await connection.OpenAsync(cancellationToken);
                        connected = true;
                    }
                }
                catch(MySqlException)
                {
                    retries++;
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
                }
            }

            if(!connected)
            {
                throw new ApplicationException("Unable to connect to the database server.");
            }
        }
    }
}
