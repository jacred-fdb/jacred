using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Engine.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Engine.Background
{
    public class StatsWorker : BackgroundService
    {
        readonly ILogger<StatsWorker> _logger;

        public StatsWorker(ILogger<StatsWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await StatsCron.Run(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "stats worker terminated unexpectedly");
            }
        }
    }
}
