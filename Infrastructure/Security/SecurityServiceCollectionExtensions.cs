using Microsoft.Extensions.DependencyInjection;

namespace JacRed.Infrastructure.Security
{
    public static class SecurityServiceCollectionExtensions
    {
        public static IServiceCollection AddJacRedSecurity(this IServiceCollection services)
        {
            services.AddSingleton<IJacRedApiKeyValidator, JacRedApiKeyValidator>();
            services.AddSingleton<IJacRedDevKeyValidator, JacRedDevKeyValidator>();
            services.AddSingleton<IJacRedAccessEvaluator, JacRedAccessEvaluator>();
            return services;
        }
    }
}
