using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Tracks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Background
{
    public class TracksWorker : BackgroundService
    {
        readonly ILogger<TracksWorker> _logger;

        public TracksWorker(ILogger<TracksWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("tracks worker started");
            var tasks = new Task[5];
            for (int i = 0; i < 5; i++)
            {
                int typetask = i + 1;
                tasks[i] = RunTypetask(typetask, stoppingToken);
            }

            await Task.WhenAll(tasks);
        }

        async Task RunTypetask(int typetask, CancellationToken stoppingToken)
        {
            try
            {
                await TracksCron.Run(typetask, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "tracks worker typetask={Typetask} terminated unexpectedly", typetask);
            }
        }
    }
}
