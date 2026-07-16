using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Tracks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Background
{
    public class FastDbRefreshWorker : BackgroundService
    {
        readonly IFastDbIndex _fastDbIndex;
        readonly ILogger<FastDbRefreshWorker> _logger;
        long _lastFileTimeSum;
        int _lastMasterCount = -1;
        int _lastKeyXor;

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

            try
            {
                _fastDbIndex.Rebuild();
                CaptureMasterFingerprint();
            }
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

                try
                {
                    if (!MasterDbChanged())
                    {
                        _logger.LogDebug("fastdb skip rebuild (masterDb unchanged)");
                        continue;
                    }

                    _fastDbIndex.Rebuild();
                    CaptureMasterFingerprint();
                }
                catch (Exception ex) { _logger.LogError(ex, "fastdb periodic rebuild"); }
            }
        }

        void CaptureMasterFingerprint()
        {
            ComputeFingerprint(out _lastMasterCount, out _lastFileTimeSum, out _lastKeyXor);
        }

        bool MasterDbChanged()
        {
            ComputeFingerprint(out int count, out long sum, out int keyXor);
            return count != _lastMasterCount || sum != _lastFileTimeSum || keyXor != _lastKeyXor;
        }

        static void ComputeFingerprint(out int count, out long fileTimeSum, out int keyXor)
        {
            var arr = FileDB.masterDb.ToArray();
            count = arr.Length;
            fileTimeSum = 0;
            keyXor = 0;
            foreach (var item in arr)
            {
                fileTimeSum += item.Value.fileTime;
                keyXor ^= StringComparer.Ordinal.GetHashCode(item.Key);
            }
        }
    }
}
