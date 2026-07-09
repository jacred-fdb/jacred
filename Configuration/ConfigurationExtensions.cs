using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JacRed.Configuration
{
    public static class ConfigurationExtensions
    {
        public static IServiceCollection AddJacRedConfiguration(this IServiceCollection services)
        {
            AppConfigurationProvider.EnsureInitialized();
            var provider = AppConfigurationProvider.Instance;
            services.AddSingleton(provider);
            services.AddSingleton<IOptionsMonitor<AppOptions>>(provider);
            services.AddHostedService<AppConfigurationReloadWorker>();
            return services;
        }
    }
}
