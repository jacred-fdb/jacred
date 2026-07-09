using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Application.Index;
using JacRed.Infrastructure.Tracks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Background
{
    public class FastDbRefreshWorker : BackgroundService
    {
        readonly IFastDbIndex _fastDbIndex;
        readonly ILogger<FastDbRefreshWorker> _logger;

        public FastDbRefreshWorker(IFastDbIndex fastDbIndex, ILogger<FastDbRefreshWorker> logger)
        {
            _fastDbIndex = fastDbIndex;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("fastdb worker started");
            try { TracksDB.StartupInit(); }
            catch (IOException ex) { _logger.LogWarning(ex, "tracks startup"); }
            catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "tracks startup"); }

            try { _fastDbIndex.Rebuild(); }
            catch (Exception ex) { _logger.LogError(ex, "fastdb startup rebuild"); }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try { _fastDbIndex.Rebuild(); }
                catch (Exception ex) { _logger.LogError(ex, "fastdb periodic rebuild"); }
            }
        }
    }
}
