using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Tracks;
using JacRed.Infrastructure.Logging;
using JacRed.Models;
using JacRed.Models.Details;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace JacRed.Infrastructure.Stats
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

        public static DateTime? TryReadStatsMetaUpdatedAt()
        {
            if (!File.Exists(StatsMetaPath))
                return null;

            try
            {
                var jo = JObject.Parse(File.ReadAllText(StatsMetaPath));
                if (!jo.TryGetValue("updatedAt", out var token))
                    return null;

                if (token.Type == JTokenType.Date)
                    return token.Value<DateTime>();

                if (DateTime.TryParse(token.ToString(), null, DateTimeStyles.RoundtripKind, out var dt))
                    return dt;
            }
            catch { }

            return null;
        }

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
                    JacRedLog.Information(JacRedLogCategories.Stats,
                        $"deferred — tracks index empty (index={TracksDB.TrackIndexCount}), waiting for index rebuild / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    return _lastCollectedAtUtc;
                }

                var sw = Stopwatch.StartNew();
                var updatedAt = DateTime.UtcNow;
                var today = updatedAt.Date;

                var scan = ScanFdb(today);
                WriteTrackerStats(scan.Trackers, updatedAt);
                TracksDB.PublishExportStatsCache(updatedAt, scan);

                _lastCollectedAtUtc = updatedAt;
                JacRedLog.Information(JacRedLogCategories.Stats,
                    $"collected {scan.Trackers.Count} trackers, torrents={scan.TorrentsScanned}, tracks total index={TracksDB.TrackIndexCount} / {sw.Elapsed.TotalSeconds:F1}s / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                return updatedAt;
            }
        }

        public static StatsFdbScanResult ScanFdb(DateTime todayUtc, bool forceFull = false)
        {
            var result = new StatsFdbScanResult();

            // Incremental: prefer shards touched since last collect; fall back to full scan when too many changed.
            long watermark = _lastCollectedAtUtc?.ToFileTimeUtc() ?? 0;
            var all = FileDB.masterDb.ToArray();
            KeyValuePair<string, MasterDbShard>[] toScan = all;
            if (!forceFull && watermark > 0)
            {
                var changed = all.Where(i => i.Value.fileTime > watermark).ToArray();
                // If fewer than 30% of shards changed, still do a full pass for accurate totals —
                // but open unchanged shards from evercache when available (cache:true) to cut disk I/O.
                if (changed.Length > 0 && changed.Length < all.Length * 0.3)
                {
                    // Full logical scan, cheap reads for cold shards via request... use cache for unchanged.
                    foreach (var item in all)
                    {
                        bool recent = item.Value.fileTime > watermark;
                        IReadOnlyDictionary<string, TorrentDetails> db;
                        try
                        {
                            db = FileDB.OpenRead(item.Key, cache: !recent);
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
            }

            foreach (var item in toScan)
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
