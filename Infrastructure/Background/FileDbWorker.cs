using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Background
{
    public class FileDbWorker : BackgroundService
    {
        readonly ILogger<FileDbWorker> _logger;

        public FileDbWorker(ILogger<FileDbWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var cron = RunCron(stoppingToken);
            var cronFast = RunCronFast(stoppingToken);
            await Task.WhenAll(cron, cronFast);
        }

        async Task RunCron(CancellationToken stoppingToken)
        {
            try
            {
                await FileDB.Cron(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "fdb cron worker terminated unexpectedly");
            }
        }

        async Task RunCronFast(CancellationToken stoppingToken)
        {
            try
            {
                await FileDB.CronFast(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "fdb cron fast worker terminated unexpectedly");
            }
        }
    }
}
