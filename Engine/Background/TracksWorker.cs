using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Engine.Background
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
