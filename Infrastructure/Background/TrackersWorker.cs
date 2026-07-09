using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Background;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Background
{
    public class TrackersWorker : BackgroundService
    {
        readonly ILogger<TrackersWorker> _logger;

        public TrackersWorker(ILogger<TrackersWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await TrackersCron.Run(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "trackers worker terminated unexpectedly");
            }
        }
    }
}
