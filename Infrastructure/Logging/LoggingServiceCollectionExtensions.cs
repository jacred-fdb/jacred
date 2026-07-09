using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Logging
{
    public static class LoggingServiceCollectionExtensions
    {
        public static IServiceCollection AddJacRedLogging(this IServiceCollection services)
        {
            services.AddSingleton<IConfigureOptions<ConsoleLoggerOptions>, JacRedConsoleFormatterConfigureOptions>();
            services.AddSingleton<ConsoleFormatter, JacRedConsoleFormatter>();

            services.AddLogging(builder =>
            {
                builder.AddConsole(options =>
                {
                    options.FormatterName = JacRedConsoleFormatter.FormatterName;
                });
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddFilter("JacRed", LogLevel.Debug);
            });

            return services;
        }
    }
}
