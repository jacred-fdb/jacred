using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Tracks;
using JacRed.Models.Details;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace JacRed.Infrastructure
{
    /// <summary>
    /// Single-pass FDB scan: tracker stats (stats.json) + tracks export stats (tracks-stats.json) with one updatedAt.
    /// </summary>
    public static class StatsCollector
    {
        public const string StatsPath = "Data/temp/stats.json";
        public const string StatsMetaPath = "Data/temp/stats-meta.json";

        static readonly object _collectLock = new object();
        static DateTime? _lastCollectedAtUtc;

        public static DateTime? LastCollectedAtUtc => _lastCollectedAtUtc;

        /// <summary>
        /// Full collect: one FDB pass, write stats.json + tracks-stats.json.
        /// Returns null when deferred (empty track index). Use <paramref name="force"/> to skip deferral.
        /// </summary>
        public static DateTime? CollectAndWrite(bool force = false)
        {
            lock (_collectLock)
            {
                if (!force && !TracksDB.IsTrackIndexReadyForStats())
                {
                    Console.WriteLine(
                        $"stats: deferred — tracks index empty (index={TracksDB.TrackIndexCount}), waiting for index rebuild / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    return _lastCollectedAtUtc;
                }

                var sw = Stopwatch.StartNew();
                var updatedAt = DateTime.UtcNow;
                var today = updatedAt.Date;

                var scan = ScanFdb(today);
                WriteTrackerStats(scan.Trackers, updatedAt);
                TracksDB.PublishExportStatsCache(updatedAt, scan);

                _lastCollectedAtUtc = updatedAt;
                Console.WriteLine(
                    $"stats: collected {scan.Trackers.Count} trackers, tracks total index={TracksDB.TrackIndexCount} / {sw.Elapsed.TotalSeconds:F1}s / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                return updatedAt;
            }
        }

        public static StatsFdbScanResult ScanFdb(DateTime todayUtc)
        {
            var result = new StatsFdbScanResult();

            foreach (var item in FileDB.masterDb.ToArray())
            {
                IReadOnlyDictionary<string, TorrentDetails> db;
                try
                {
                    db = FileDB.OpenRead(item.Key, cache: false);
                }
                catch
                {
                    result.TorrentDbErrors++;
                    continue;
                }

                if (db == null)
                    continue;

                foreach (var t in db.Values)
                {
                    if (t == null || string.IsNullOrEmpty(t.trackerName))
                        continue;

                    try
                    {
                        AccumulateTracker(result, t, todayUtc);
                        result.TorrentsScanned++;

                        if (t.ffprobe != null && t.ffprobe.Count > 0 && !string.IsNullOrEmpty(t.magnet))
                        {
                            if (TracksDB.TryGetInfohashFromMagnet(t.magnet, out var hash))
                                result.FfprobeHashesFromFdb.Add(hash);
                            else
                                result.MagnetErrors++;
                        }
                    }
                    catch { }
                }
            }

            return result;
        }

        static void AccumulateTracker(StatsFdbScanResult result, TorrentDetails t, DateTime todayUtc)
        {
            if (!result.Trackers.TryGetValue(t.trackerName, out var row))
            {
                row = new TrackerStatsRow { LastNewTor = t.createTime };
                result.Trackers.Add(t.trackerName, row);
            }

            row.AllTorrents++;

            if (t.createTime > row.LastNewTor)
                row.LastNewTor = t.createTime;

            if (t.createTime >= todayUtc)
                row.NewTor++;

            if (t.updateTime >= todayUtc)
                row.Update++;

            if (t.checkTime >= todayUtc)
                row.Check++;

            if (!TracksDB.theBad(t.types) && !string.IsNullOrEmpty(t.magnet))
            {
                if (t.ffprobe_tryingdata >= 3)
                    row.TrkError++;
                else if (TracksDB.HasTrackForTorrent(t))
                    row.TrkConfirm++;
                else
                    row.TrkWait++;
            }
        }

        static void WriteTrackerStats(Dictionary<string, TrackerStatsRow> trackers, DateTime updatedAt)
        {
            var payload = trackers
                .OrderByDescending(i => i.Value.AllTorrents)
                .Select(i => new
                {
                    trackerName = i.Key,
                    lastnewtor = i.Value.LastNewTor.ToString("dd.MM.yyyy"),
                    newtor = i.Value.NewTor,
                    update = i.Value.Update,
                    check = i.Value.Check,
                    alltorrents = i.Value.AllTorrents,
                    tracks = new
                    {
                        wait = i.Value.TrkWait,
                        confirm = i.Value.TrkConfirm,
                        skip = i.Value.TrkError
                    }
                });

            WriteTextAtomic(StatsPath, JsonConvert.SerializeObject(payload, Formatting.Indented));

            WriteTextAtomic(StatsMetaPath, JsonConvert.SerializeObject(new
            {
                updatedAt = updatedAt,
                updatedAtLocal = updatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                trackerCount = trackers.Count
            }, Formatting.Indented));
        }

        public static void WriteTextAtomic(string path, string content)
        {
            var fullPath = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempPath = fullPath + ".tmp";
            File.WriteAllText(tempPath, content);

            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, null);
            else
                File.Move(tempPath, fullPath);
        }
    }

    public sealed class StatsFdbScanResult
    {
        public Dictionary<string, TrackerStatsRow> Trackers { get; } = new Dictionary<string, TrackerStatsRow>(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> FfprobeHashesFromFdb { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int TorrentsScanned { get; set; }

        public int TorrentDbErrors { get; set; }

        public int MagnetErrors { get; set; }
    }

    public sealed class TrackerStatsRow
    {
        public DateTime LastNewTor { get; set; }

        public int NewTor { get; set; }

        public int Update { get; set; }

        public int Check { get; set; }

        public int AllTorrents { get; set; }

        public int TrkConfirm { get; set; }

        public int TrkWait { get; set; }

        public int TrkError { get; set; }
    }
}
