using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace JacRed.Configuration
{
    /// <summary>Polls init.yaml / init.conf every 10 seconds for hot-reload (replaces AppInit static ThreadPool loop).</summary>
    public sealed class AppConfigurationReloadWorker : BackgroundService
    {
        readonly AppConfigurationProvider _provider;

        public AppConfigurationReloadWorker(AppConfigurationProvider provider)
        {
            _provider = provider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                _provider.RefreshIfChanged();
            }
        }
    }
}
