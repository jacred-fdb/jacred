using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Stats;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using MonoTorrent;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Tracks
{
    public static class TracksDB
    {
        /// <summary>Legacy entry point — calls <see cref="StartupInit"/>.</summary>
        public static void Configuration() => StartupInit();

        /// <summary>
        /// Fast startup: load stats cache + compact tracks index. Full index rebuild runs in background.
        /// Individual tracks are loaded on demand via <see cref="Get"/>.
        /// </summary>
        public static void StartupInit()
        {
            var sw = Stopwatch.StartNew();
            JacRedLog.Information(JacRedLogCategories.Tracks, $"startup init / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            TracksStatsCache.TryLoadStatsCacheOnStartup();
            TracksIndexManager.LoadTracksIndex();

            JacRedLog.Information(JacRedLogCategories.Tracks, $"startup init done / index={TrackIndexCount} / {sw.Elapsed.TotalSeconds:F1}s");

            TracksIndexManager.ScheduleIndexRebuildIfNeeded();
            TracksIndexManager.StartIndexPersistLoop();
        }

        public static bool HasTrackOnDisk(string infohash)
        {
            infohash = TracksPathResolver.NormalizeInfohash(infohash);
            if (TracksIndexManager.TrackIndex.ContainsKey(infohash))
                return true;

            return TracksPathResolver.ResolveTrackPath(infohash) != null;
        }

        public static bool HasTrackForTorrent(TorrentDetails t)
        {
            if (t?.ffprobe != null && t.ffprobe.Count > 0)
                return true;

            if (string.IsNullOrEmpty(t?.magnet))
                return false;

            if (!TryGetInfohashFromMagnet(t.magnet, out var infohash))
                return false;

            if (TracksAnalyzer.Database.TryGetValue(infohash, out var model) && model?.streams != null && model.streams.Count > 0)
                return true;

            if (TracksIndexManager.TrackIndex.ContainsKey(infohash))
                return true;

            var path = TracksPathResolver.ResolveTrackPath(infohash);
            return path != null && TracksPathResolver.TrackFileHasStreams(path);
        }

        public static bool IsTrackIndexReadyForStats()
        {
            if (TrackIndexCount > 0)
                return true;

            if (!Directory.Exists("Data/tracks"))
                return true;

            try
            {
                return Directory.GetDirectories("Data/tracks").Length == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetInfohashFromMagnet(string magnet, out string infohash)
        {
            infohash = null;
            if (string.IsNullOrEmpty(magnet))
                return false;

            try
            {
                infohash = TracksPathResolver.NormalizeInfohash(MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex());
                return TracksPathResolver.IsValidInfohash(infohash);
            }
            catch
            {
                return false;
            }
        }

        public static int TrackIndexCount => TracksIndexManager.TrackIndexCount;

        public static DateTime? GetExportStatsUpdatedAt() => TracksStatsCache.StatsCacheUpdatedAt;
        public static bool LastExportStatsFromCache => TracksStatsCache.LastExportStatsFromCache;

        public static bool theBad(string[] types) => TracksAnalyzer.theBad(types);

        public static List<ffStream> Get(string magnet, string[] types = null, bool memoryOnly = false) =>
            TracksAnalyzer.Get(magnet, types, memoryOnly);

        public static System.Threading.Tasks.Task Add(string magnet, int currentAttempt, string[] types = null, string torrentKey = null, int typetask = 1) =>
            TracksAnalyzer.Add(magnet, currentAttempt, types, torrentKey, typetask);

        public static void Log(string message, int? typetask = null, LogLevel? level = null) =>
            TracksLogging.Log(message, typetask, level);

        public static void LogToFile(string message, int? typetask = null) =>
            TracksLogging.LogToFile(message, typetask);

        public static HashSet<string> Languages(TorrentDetails t, List<ffStream> streams) =>
            TracksAnalyzer.Languages(t, streams);

        public static Dictionary<string, FfprobeModel> CollectAll(bool includeTorrentDb = true) =>
            TracksExportService.CollectAll(includeTorrentDb);

        public static DateTime PublishExportStatsCache(DateTime updatedAt, StatsFdbScanResult scan) =>
            TracksStatsCache.PublishExportStatsCache(updatedAt, scan);

        public static DateTime RefreshExportStatsCache() =>
            TracksStatsCache.RefreshExportStatsCache();

        public static TracksExportStats GetExportStats(bool includeTorrentDb = true, bool refresh = false) =>
            TracksStatsCache.GetExportStats(includeTorrentDb, refresh);

        public static TracksExportJobStatus GetExportJobStatus() =>
            TracksExportService.GetExportJobStatus();

        public static bool TryStartExport(string outputDir = "Data/tracks-export", bool includeTorrentDb = true) =>
            TracksExportService.TryStartExport(outputDir, includeTorrentDb);

        public static TracksExportResult ExportAll(string outputDir = "Data/tracks-export", bool dryRun = false, bool includeTorrentDb = true, TracksExportJobStatus progress = null) =>
            TracksExportService.ExportAll(outputDir, dryRun, includeTorrentDb, progress);

        public static TracksBackfillResult BackfillTracks(string tracksDir = "Data/tracks", bool dryRun = false, bool includeTorrentDb = true, bool migrateLegacy = true) =>
            TracksExportService.BackfillTracks(tracksDir, dryRun, includeTorrentDb, migrateLegacy);

        /// <summary>Класс для десериализации информации о торренте (tsuri API).</summary>
        public class TorrentInfo
        {
            public string title { get; set; }
            public string category { get; set; }
            public string poster { get; set; }
            public long timestamp { get; set; }
            public string name { get; set; }
            public string hash { get; set; }
            public int stat { get; set; }
            public string stat_string { get; set; }
            public List<TorrentFileStat> file_stats { get; set; }
        }

        /// <summary>File entry from TorrServer torrent status (1-based ids).</summary>
        public class TorrentFileStat
        {
            public int id { get; set; }
            public string path { get; set; }
            public long length { get; set; }
        }
    }
}
