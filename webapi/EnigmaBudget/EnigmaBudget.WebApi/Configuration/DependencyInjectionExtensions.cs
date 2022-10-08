using AutoMapper;
using EnigmaBudget.Infrastructure;
using EnigmaBudget.Infrastructure.Auth;
using EnigmaBudget.Infrastructure.SendInBlue;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace EnigmaBudget.WebApi.Configuration
{
    public static class DependencyInjectionExtensions
    {
        public static IServiceCollection RegisterAutoMappers(this IServiceCollection services)
        {
            services.AddAutoMapper(Assembly.Load("EnigmaBudget.Infrastructure"));

            return services;
        }

        public static IServiceCollection RegisterRepositories(this IServiceCollection services)
        {
            //services.AddTransient<IRepository, Repository>();
            return services;
        }

        public static IServiceCollection RegisterApplicationServices(this IServiceCollection services)
        {
            services.AddTransient<IEmailApiService, SendInBlueEmailApiService>();
            services.AddTransient<IAuthService, AuthService>();

            return services;
        }
    }
}
