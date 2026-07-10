using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Stats;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Utils;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Tracks
{
    internal static class TracksIndexManager
    {
        internal const string TracksIndexPath = "Data/temp/tracks-index.bz";

        internal static readonly ConcurrentDictionary<string, byte> TrackIndex =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        static int _indexDirty;
        static int _indexBuildRunning;
        static readonly object _indexPersistLock = new object();

        internal static int TrackIndexCount => TrackIndex.Count;

        internal static void LoadTracksIndex()
        {
            if (!File.Exists(TracksIndexPath))
                return;

            try
            {
                var data = JsonStream.Read<TracksIndexFile>(TracksIndexPath);
                if (data?.hashes == null || data.hashes.Count == 0)
                    return;

                foreach (var hash in data.hashes)
                {
                    if (TracksPathResolver.IsValidInfohash(hash))
                        TrackIndex.TryAdd(TracksPathResolver.NormalizeInfohash(hash), 0);
                }

                JacRedLog.Information(JacRedLogCategories.TracksIndex, $"loaded {TrackIndex.Count} hashes (built {data.builtAt:yyyy-MM-dd HH:mm:ss} UTC)");
            }
            catch (Exception ex)
            {
                JacRedLog.Information(JacRedLogCategories.TracksIndex, $"load error / {ex.Message}");
            }
        }

        internal static void PersistTracksIndex()
        {
            lock (_indexPersistLock)
            {
                try
                {
                    if (!Directory.Exists("Data/temp"))
                        Directory.CreateDirectory("Data/temp");

                    var file = new TracksIndexFile
                    {
                        builtAt = DateTime.UtcNow,
                        hashes = TrackIndex.Keys.ToList()
                    };

                    JsonStream.Write(TracksIndexPath, file);
                    Interlocked.Exchange(ref _indexDirty, 0);
                    JacRedLog.Information(JacRedLogCategories.TracksIndex, $"saved {file.hashes.Count} hashes / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    JacRedLog.Information(JacRedLogCategories.TracksIndex, $"save error / {ex.Message}");
                }
            }
        }

        internal static void RegisterTrackHash(string infohash)
        {
            infohash = TracksPathResolver.NormalizeInfohash(infohash);
            if (!TracksPathResolver.IsValidInfohash(infohash))
                return;

            if (TrackIndex.TryAdd(infohash, 0))
                Interlocked.Exchange(ref _indexDirty, 1);
        }

        internal static void ScheduleIndexRebuildIfNeeded()
        {
            if (TrackIndex.Count > 0)
                return;

            if (!Directory.Exists("Data/tracks"))
                return;

            try
            {
                if (Directory.GetDirectories("Data/tracks").Length == 0)
                    return;
            }
            catch (IOException ex)
            {
                JacRedLog.Information(JacRedLogCategories.TracksIndex, $"schedule rebuild skipped / {ex.Message}");
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                JacRedLog.Information(JacRedLogCategories.TracksIndex, $"schedule rebuild skipped / {ex.Message}");
                return;
            }

            ThreadPool.QueueUserWorkItem(_ => _ = BuildTracksIndexAsync());
        }

        internal static void StartIndexPersistLoop()
        {
            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(30));
                    if (Volatile.Read(ref _indexDirty) == 0)
                        continue;

                    PersistTracksIndex();
                }
            });
        }

        internal static async Task BuildTracksIndexAsync()
        {
            if (Interlocked.CompareExchange(ref _indexBuildRunning, 1, 0) != 0)
                return;

            try
            {
                var sw = Stopwatch.StartNew();
                JacRedLog.Information(JacRedLogCategories.TracksIndex, $"rebuild start / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                var built = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                await Task.Run(() => ScanTracksDirForIndex("Data/tracks", built));

                foreach (var hash in built.Keys)
                    TrackIndex.TryAdd(hash, 0);

                Interlocked.Exchange(ref _indexDirty, 1);
                PersistTracksIndex();

                JacRedLog.Information(JacRedLogCategories.TracksIndex, $"rebuild done / count={TrackIndex.Count} / {sw.Elapsed.TotalMinutes:F1} min");

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { StatsCollector.CollectAndWrite(); }
                    catch (Exception ex) { JacRedLog.Error(JacRedLogCategories.Stats, $"post-index collect error / {ex.Message}"); }
                });
            }
            catch (Exception ex)
            {
                JacRedLog.Information(JacRedLogCategories.TracksIndex, $"rebuild error / {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _indexBuildRunning, 0);
            }
        }

        internal static void ScanTracksDirForIndex(string tracksDir, ConcurrentDictionary<string, byte> target)
        {
            if (!Directory.Exists(tracksDir))
                return;

            foreach (var folder1 in Directory.GetDirectories(tracksDir))
            {
                foreach (var folder2 in Directory.GetDirectories(folder1))
                {
                    foreach (var file in Directory.GetFiles(folder2))
                    {
                        string filename = Path.GetFileName(file);
                        if (TracksPathResolver.ShouldSkipLegacyTrackFile(folder2, filename))
                            continue;

                        string infohash = TracksPathResolver.InfohashFromTrackRelPath(
                            Path.GetFileName(folder1),
                            Path.GetFileName(folder2),
                            filename);

                        if (!TracksPathResolver.IsValidInfohash(infohash))
                            continue;

                        if (!TracksPathResolver.TrackFileHasStreams(file))
                            continue;

                        target.TryAdd(TracksPathResolver.NormalizeInfohash(infohash), 0);
                    }
                }
            }
        }
    }
}
