using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Stats;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JacRed.Infrastructure.Tracks
{
    internal static class TracksStatsCache
    {
        internal const string TracksStatsPath = "Data/temp/tracks-stats.json";

        static readonly object _statsCacheLock = new object();
        static DateTime? _statsCacheUpdatedAt;
        static bool _lastExportStatsFromCache;

        internal static DateTime? StatsCacheUpdatedAt => _statsCacheUpdatedAt;
        internal static bool LastExportStatsFromCache => _lastExportStatsFromCache;

        internal static void TryLoadStatsCacheOnStartup()
        {
            TryLoadStatsCache(true, out _, out _);
        }

        internal static TracksExportStats BuildExportStats(bool includeTorrentDb, StatsFdbScanResult scan)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stats = new TracksExportStats();

            ApplyTrackIndexToSeen(seen, stats);

            foreach (var item in TracksAnalyzer.Database)
            {
                if (item.Value?.streams == null || item.Value.streams.Count == 0)
                    continue;

                if (seen.Add(item.Key))
                    stats.fromMemory++;
            }

            if (includeTorrentDb)
            {
                if (scan != null)
                {
                    stats.torrentsScanned = scan.TorrentsScanned;
                    stats.torrentDbErrors = scan.TorrentDbErrors;
                    stats.magnetErrors = scan.MagnetErrors;

                    foreach (var hash in scan.FfprobeHashesFromFdb)
                    {
                        if (seen.Add(hash))
                            stats.fromTorrentDb++;
                    }
                }
                else
                {
                    CollectStatsFromTorrentDb(seen, stats);
                }
            }

            stats.total = seen.Count;
            return stats;
        }

        static void ApplyTrackIndexToSeen(HashSet<string> seen, TracksExportStats stats)
        {
            if (TracksIndexManager.TrackIndexCount > 0)
            {
                stats.filesScanned = TracksIndexManager.TrackIndexCount;
                foreach (var key in TracksIndexManager.TrackIndex.Keys)
                {
                    if (seen.Add(key))
                        stats.fromTracksFiles++;
                }

                return;
            }

            CollectStatsFromTracksDir("Data/tracks", seen, stats);
        }

        static void CollectStatsFromTracksDir(string tracksDir, HashSet<string> seen, TracksExportStats stats)
        {
            if (!Directory.Exists(tracksDir))
                return;

            foreach (var folder1 in Directory.GetDirectories(tracksDir))
            {
                foreach (var folder2 in Directory.GetDirectories(folder1))
                {
                    foreach (var file in Directory.GetFiles(folder2))
                    {
                        stats.filesScanned++;

                        string filename = Path.GetFileName(file);
                        if (TracksPathResolver.ShouldSkipLegacyTrackFile(folder2, filename))
                            continue;

                        string infohash = TracksPathResolver.InfohashFromTrackRelPath(
                            Path.GetFileName(folder1),
                            Path.GetFileName(folder2),
                            filename);

                        if (!TracksPathResolver.IsValidInfohash(infohash))
                        {
                            stats.invalidPath++;
                            continue;
                        }

                        if (!TracksPathResolver.TrackFileHasStreams(file))
                        {
                            stats.emptyStreams++;
                            continue;
                        }

                        if (seen.Add(infohash))
                            stats.fromTracksFiles++;
                    }
                }
            }
        }

        static void CollectStatsFromTorrentDb(HashSet<string> seen, TracksExportStats stats)
        {
            foreach (var item in FileDB.masterDb.Keys)
            {
                IReadOnlyDictionary<string, TorrentDetails> db;
                try
                {
                    db = FileDB.OpenRead(item, cache: false);
                }
                catch
                {
                    stats.torrentDbErrors++;
                    continue;
                }

                if (db == null)
                    continue;

                foreach (var torrent in db.Values)
                {
                    stats.torrentsScanned++;

                    if (torrent?.ffprobe == null || torrent.ffprobe.Count == 0 || string.IsNullOrEmpty(torrent.magnet))
                        continue;

                    string infohash;
                    try
                    {
                        infohash = TracksPathResolver.NormalizeInfohash(MonoTorrent.MagnetLink.Parse(torrent.magnet).InfoHashes.V1OrV2.ToHex());
                    }
                    catch
                    {
                        stats.magnetErrors++;
                        continue;
                    }

                    if (!TracksPathResolver.IsValidInfohash(infohash))
                    {
                        stats.magnetErrors++;
                        continue;
                    }

                    if (seen.Contains(infohash))
                        continue;

                    if (seen.Add(infohash))
                        stats.fromTorrentDb++;
                }
            }
        }

        /// <summary>
        /// Writes tracks-stats.json from a shared FDB scan (see <see cref="StatsCollector"/>).
        /// </summary>
        internal static DateTime PublishExportStatsCache(DateTime updatedAt, StatsFdbScanResult scan)
        {
            lock (_statsCacheLock)
            {
                var cache = new TracksStatsCacheFile
                {
                    updatedAt = updatedAt,
                    entries = new List<TracksStatsCacheEntry>
                    {
                        new TracksStatsCacheEntry { includeTorrentDb = true, stats = BuildExportStats(true, scan) },
                        new TracksStatsCacheEntry { includeTorrentDb = false, stats = BuildExportStats(false, scan) }
                    }
                };

                WriteStatsCacheFile(cache);
                JacRedLog.Information(JacRedLogCategories.TracksStats, $"wrote cache to {TracksStatsPath} / total={cache.entries[0].stats.total} / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                return updatedAt;
            }
        }

        internal static bool TryLoadStatsCache(bool includeTorrentDb, out TracksExportStats stats, out DateTime? updatedAt)
        {
            stats = null;
            updatedAt = _statsCacheUpdatedAt;

            try
            {
                if (!File.Exists(TracksStatsPath))
                    return false;

                var cache = JsonConvert.DeserializeObject<TracksStatsCacheFile>(File.ReadAllText(TracksStatsPath));
                if (cache?.entries == null || cache.entries.Count == 0)
                    return false;

                updatedAt = cache.updatedAt;
                _statsCacheUpdatedAt = cache.updatedAt;

                var entry = cache.entries.FirstOrDefault(e => e.includeTorrentDb == includeTorrentDb);
                if (entry?.stats == null)
                    return false;

                stats = entry.stats;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void WriteStatsCacheFile(TracksStatsCacheFile cache)
        {
            StatsCollector.WriteTextAtomic(TracksStatsPath, JsonConvert.SerializeObject(cache, Formatting.Indented));
            _statsCacheUpdatedAt = cache.updatedAt;
        }

        internal static DateTime RefreshExportStatsCache() =>
            StatsCollector.CollectAndWrite(force: true) ?? DateTime.UtcNow;

        internal static TracksExportStats GetExportStats(bool includeTorrentDb = true, bool refresh = false)
        {
            if (!refresh && TryLoadStatsCache(includeTorrentDb, out var cached, out _))
            {
                _lastExportStatsFromCache = true;
                return cached;
            }

            lock (_statsCacheLock)
            {
                if (!refresh && TryLoadStatsCache(includeTorrentDb, out cached, out _))
                {
                    _lastExportStatsFromCache = true;
                    return cached;
                }
            }

            _lastExportStatsFromCache = false;
            StatsCollector.CollectAndWrite(force: true);

            lock (_statsCacheLock)
            {
                if (TryLoadStatsCache(includeTorrentDb, out cached, out _))
                    return cached;

                return BuildExportStats(includeTorrentDb, null);
            }
        }
    }
}
