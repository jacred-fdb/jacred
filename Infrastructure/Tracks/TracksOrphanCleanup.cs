using System;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Tracks
{
    /// <summary>
    /// Removes TorrServer torrents left in trackscategory after failed rem or crash.
    /// </summary>
    internal static class TracksOrphanCleanup
    {
        public static async Task RunLoopAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int intervalMin = Math.Max(1, AppInit.conf?.tracksorphansweepmin ?? 15);
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(intervalMin), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (!AppInit.conf.tracks)
                    continue;

                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    TracksDB.Log($"orphan sweep error: {ex.Message}");
                }
            }
        }

        internal static async Task RunOnceAsync(CancellationToken token)
        {
            if (AppInit.conf?.tsuri == null || AppInit.conf.tsuri.Length == 0)
                return;

            string category = AppInit.conf.trackscategory;
            if (string.IsNullOrEmpty(category))
                return;

            var inFlight = TracksAnalyzer.GetInFlightHashes();
            int removed = 0;

            foreach (var tsuri in AppInit.conf.tsuri)
            {
                if (string.IsNullOrWhiteSpace(tsuri))
                    continue;

                var (torrents, serverError) = await TracksAnalyzer.GetTorrentListForCleanup(tsuri, token)
                    .ConfigureAwait(false);

                if (serverError || torrents == null)
                    continue;

                foreach (var t in torrents)
                {
                    if (t == null || string.IsNullOrEmpty(t.hash))
                        continue;

                    if (string.IsNullOrEmpty(t.category) ||
                        !t.category.Equals(category, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (inFlight.Contains(t.hash))
                        continue;

                    bool ok = await TracksAnalyzer.RemTorrentOnServer(tsuri, t.hash, typetask: null)
                        .ConfigureAwait(false);
                    if (ok)
                    {
                        removed++;
                        TracksDB.Log($"orphan sweep: rem {t.hash}");
                    }
                }
            }

            if (removed > 0)
                TracksDB.Log($"orphan sweep done: removed {removed}");
        }
    }
}
