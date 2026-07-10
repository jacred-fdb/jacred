using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Background
{
    public class SyncWorker : BackgroundService
    {
        readonly ILogger<SyncWorker> _logger;

        public SyncWorker(ILogger<SyncWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("sync worker started");
            var torrents = RunTorrents(stoppingToken);
            var spidr = RunSpidr(stoppingToken);
            await Task.WhenAll(torrents, spidr);
        }

        async Task RunTorrents(CancellationToken stoppingToken)
        {
            try
            {
                await SyncCron.Torrents(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "sync torrents worker terminated unexpectedly");
            }
        }

        async Task RunSpidr(CancellationToken stoppingToken)
        {
            try
            {
                await SyncCron.Spidr(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "sync spidr worker terminated unexpectedly");
            }
        }
    }
}
