using EnigmaBudget.Application;
using EnigmaBudget.Application.Services;
using EnigmaBudget.Domain.Repositories;
using EnigmaBudget.Infrastructure;
using EnigmaBudget.Infrastructure.Auth;
using EnigmaBudget.Infrastructure.SendInBlue;
using EnigmaBudget.Persistence.Repositories.EFCore;
using System.Reflection;

namespace EnigmaBudget.WebApi.Configuration
{
    public static class DependencyInjectionExtensions
    {
        public static void RegisterAutoMappers(this IServiceCollection services)
        {
            services.AddAutoMapper(Assembly.Load("EnigmaBudget.Infrastructure"));
            services.AddAutoMapper(Assembly.Load("EnigmaBudget.Persistence.Repositories.MariaDB"));
            services.AddAutoMapper(Assembly.Load("EnigmaBudget.Persistence.Repositories.EFCore"));
        }

        public static void RegisterRepositories(this IServiceCollection services)
        {
            services.AddTransient<IContextRepository, HttpContextRepository>();
            services.AddTransient<IDepositAccountRepository, DepositAccountsRepositoryEF>();
            services.AddTransient<IDepositAccountTypeRepository, DepositAccountTypesRepositoryEF>();
        }

        public static void RegisterApplicationServices(this IServiceCollection services)
        {
            services.AddTransient<IAuthService, AuthService>();
            services.AddTransient<IEmailApiService, SendInBlueEmailApiService>();
            services.AddTransient<ICountriesService, CountriesService>();
            services.AddTransient<IDepositAccountsService, DepositAccountsService>();
            services.AddTransient<IDepositAccounTypesService, DepositAccountTypesService>();
            services.AddTransient<IBalancesService, BalancesService>();
        }
    }
}
