using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Application.Maintenance;
using JacRed.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Background
{
    /// <summary>After process start, continue incomplete ParseAll cycles (deploy / maintenance restart).</summary>
    public sealed class ParseAllResumeWorker : BackgroundService
    {
        readonly ParseAllResumeService _resume;
        readonly ILogger<ParseAllResumeWorker> _logger;

        public ParseAllResumeWorker(ParseAllResumeService resume, ILogger<ParseAllResumeWorker> logger)
        {
            _resume = resume;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested)
                    return;

                _logger.LogInformation("ParseAll resume after startup");
                await _resume.ResumeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Trackers, $"ParseAll resume on startup failed: {ex.Message}");
                _logger.LogError(ex, "ParseAll resume on startup failed");
            }
        }
    }
}
