using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Application.Index;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Trackers;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;
using Newtonsoft.Json;

namespace JacRed.Application.Maintenance
{
    public sealed class FdbMaintenanceService : IFdbMaintenanceService
    {
        const string ReportPath = "Data/temp/maintenance-last.json";
        static readonly TimeSpan MaxDuration = TimeSpan.FromHours(6);

        readonly IFastDbIndex _fastDbIndex;
        readonly TrackerWorkFlag _workFlag = new TrackerWorkFlag();

        volatile bool _running;
        string _currentMode;
        DateTime? _startedAtUtc;
        long _progressCurrent;
        long _progressTotal;
        string _progressDetail;
        object _lastReport;

        public FdbMaintenanceService(IFastDbIndex fastDbIndex)
        {
            _fastDbIndex = fastDbIndex;
            TryLoadLastReport();
        }

        public string Check(string mode = "report", int sampleSize = 20, bool excludeNumericXx = true)
        {
            mode = NormalizeMode(mode);
            sampleSize = ClampSampleSize(sampleSize);

            return TrackerSyncHelpers.RunInBackground(
                "maintenance",
                "Check",
                _workFlag,
                checkDisabled: false,
                ct =>
                {
                    Run(mode, sampleSize, excludeNumericXx, ct, consoleProgress: false);
                    return Task.CompletedTask;
                },
                MaxDuration);
        }

        public bool Run(string mode = "report", int sampleSize = 20, bool excludeNumericXx = true,
            CancellationToken cancellationToken = default, bool consoleProgress = false)
        {
            mode = NormalizeMode(mode);
            sampleSize = ClampSampleSize(sampleSize);

            _running = true;
            _currentMode = mode;
            _startedAtUtc = DateTime.UtcNow;
            _progressDetail = "scan";
            Interlocked.Exchange(ref _progressCurrent, 0);
            Interlocked.Exchange(ref _progressTotal, FileDB.masterDb.Count);

            var sw = Stopwatch.StartNew();
            bool ok = false;
            try
            {
                LogProgress(consoleProgress,
                    $"maintenance: started mode={mode} sampleSize={sampleSize} keys={FileDB.masterDb.Count}");
                JacRedLog.Information(JacRedLogCategories.Fdb,
                    $"maintenance: Check started mode={mode} sampleSize={sampleSize}");

                var report = Scan(sampleSize, excludeNumericXx, cancellationToken, consoleProgress);
                cancellationToken.ThrowIfCancellationRequested();

                var fixedCounts = new FixedCounts();
                if (mode is "safe" or "full")
                {
                    _progressDetail = "fix";
                    LogProgress(consoleProgress, "maintenance: applying safe fixes…");
                    ApplySafeFixes(fixedCounts, cancellationToken);
                }

                if (mode == "full")
                {
                    _progressDetail = "full-fix";
                    LogProgress(consoleProgress, "maintenance: applying full fixes…");
                    ApplyFullFixes(fixedCounts, cancellationToken);
                }

                if (mode is "safe" or "full")
                {
                    _progressDetail = "save";
                    LogProgress(consoleProgress, "maintenance: saving masterDb + rebuilding fastdb…");
                    FileDB.SaveChangesToFile();
                    try { _fastDbIndex.Rebuild(); } catch { }
                }

                sw.Stop();
                report["ok"] = true;
                report["mode"] = mode;
                report["running"] = false;
                report["startedAt"] = _startedAtUtc;
                report["finishedAt"] = DateTime.UtcNow;
                report["durationSec"] = Math.Round(sw.Elapsed.TotalSeconds, 1);
                report["fixed"] = fixedCounts.ToObject();

                _lastReport = report;
                WriteReport(report);
                ok = true;

                string summary =
                    $"maintenance: finished mode={mode} duration={sw.Elapsed.TotalSeconds:F1}s " +
                    $"keys={report["totals"]} fixed={JsonConvert.SerializeObject(fixedCounts.ToObject())}";
                LogProgress(consoleProgress, summary);
                LogProgress(consoleProgress, $"maintenance: report written to {ReportPath}");
                JacRedLog.Information(JacRedLogCategories.Fdb, summary);
            }
            catch (OperationCanceledException)
            {
                LogProgress(consoleProgress, "maintenance: cancelled");
                JacRedLog.Warning(JacRedLogCategories.Fdb, "maintenance: Check cancelled");
                throw;
            }
            catch (Exception ex)
            {
                LogProgress(consoleProgress, $"maintenance: error: {ex.Message}");
                JacRedLog.Error(JacRedLogCategories.Fdb, $"maintenance: Check error: {ex.Message}");
                _lastReport = new
                {
                    ok = false,
                    mode,
                    error = ex.Message,
                    startedAt = _startedAtUtc,
                    finishedAt = DateTime.UtcNow
                };
                WriteReport(_lastReport);
                ok = false;
            }
            finally
            {
                _running = false;
                _progressDetail = null;
                _currentMode = null;
                _startedAtUtc = null;
            }

            return ok;
        }

        public object Status()
        {
            return new
            {
                ok = true,
                running = _running,
                mode = _currentMode,
                startedAt = _startedAtUtc,
                progress = _running
                    ? new
                    {
                        current = Interlocked.Read(ref _progressCurrent),
                        total = Interlocked.Read(ref _progressTotal),
                        detail = _progressDetail
                    }
                    : null,
                last = _lastReport
            };
        }

        static string NormalizeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return "report";
            mode = mode.Trim().ToLowerInvariant();
            return mode is "safe" or "full" ? mode : "report";
        }

        static int ClampSampleSize(int sampleSize)
        {
            if (sampleSize < 1)
                return 20;
            if (sampleSize > 200)
                return 200;
            return sampleSize;
        }

        static void LogProgress(bool consoleProgress, string message)
        {
            if (consoleProgress)
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        Dictionary<string, object> Scan(int sampleSize, bool excludeNumericXx, CancellationToken ct,
            bool consoleProgress = false)
        {
            var nullValue = new IssueBucket(sampleSize);
            var missingName = new IssueBucket(sampleSize);
            var missingOriginalname = new IssueBucket(sampleSize);
            var missingTrackerName = new IssueBucket(sampleSize);
            var emptySn = new IssueBucket(sampleSize);
            var emptySo = new IssueBucket(sampleSize);
            var emptyBoth = new IssueBucket(sampleSize);
            var bucketMismatch = new IssueBucket(sampleSize);
            var urlKeyMismatch = new IssueBucket(sampleSize);
            var emptyMagnetOrTypes = new IssueBucket(sampleSize);
            var missingShardFile = new IssueBucket(sampleSize);
            var emptyShardListed = new IssueBucket(sampleSize);
            var xxKeys = new IssueBucket(sampleSize);

            var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalTorrents = 0;
            var masterKeys = FileDB.masterDb.ToArray();
            Interlocked.Exchange(ref _progressTotal, masterKeys.Length);
            var lastProgressLog = Stopwatch.StartNew();

            for (int i = 0; i < masterKeys.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                Interlocked.Exchange(ref _progressCurrent, i + 1);

                if (consoleProgress && (i == 0 || (i + 1) % 1000 == 0 || lastProgressLog.Elapsed.TotalSeconds >= 5
                    || i + 1 == masterKeys.Length))
                {
                    LogProgress(true, $"maintenance: scan {i + 1}/{masterKeys.Length}");
                    lastProgressLog.Restart();
                }

                string fdbKey = masterKeys[i].Key;
                string shardPath = FileDB.PathForKey(fdbKey);
                try { expectedPaths.Add(Path.GetFullPath(shardPath)); } catch { expectedPaths.Add(shardPath); }

                if (IsXxKey(fdbKey, excludeNumericXx))
                    xxKeys.Add(new { key = fdbKey });

                if (!File.Exists(shardPath))
                {
                    missingShardFile.Add(new { fdbKey, path = shardPath });
                    continue;
                }

                var db = FileDB.OpenRead(fdbKey, cache: false);
                if (db == null || db.Count == 0)
                {
                    emptyShardListed.Add(new { fdbKey, path = shardPath });
                    continue;
                }

                foreach (var kv in db)
                {
                    totalTorrents++;
                    string url = kv.Key;
                    var t = kv.Value;

                    if (t == null)
                    {
                        nullValue.Add(new { fdbKey, url });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(t.trackerName))
                        missingTrackerName.Add(new { fdbKey, url, title = t.title });
                    if (string.IsNullOrWhiteSpace(t.name))
                        missingName.Add(new { fdbKey, url, title = t.title });
                    if (string.IsNullOrWhiteSpace(t.originalname))
                        missingOriginalname.Add(new { fdbKey, url, title = t.title });

                    bool hasEmptySn = string.IsNullOrWhiteSpace(t._sn);
                    bool hasEmptySo = string.IsNullOrWhiteSpace(t._so);
                    if (hasEmptySn && hasEmptySo)
                        emptyBoth.Add(new { fdbKey, url, title = t.title, name = t.name, originalname = t.originalname });
                    else if (hasEmptySn)
                        emptySn.Add(new { fdbKey, url, title = t.title, name = t.name, originalname = t.originalname });
                    else if (hasEmptySo)
                        emptySo.Add(new { fdbKey, url, title = t.title, name = t.name, originalname = t.originalname });

                    string expectedKey = FileDB.KeyForTorrent(t.name, t.originalname);
                    if (!string.IsNullOrEmpty(expectedKey) && !string.Equals(expectedKey, fdbKey, StringComparison.Ordinal))
                        bucketMismatch.Add(new { fdbKey, expectedKey, url, title = t.title, name = t.name, originalname = t.originalname });

                    if (!string.IsNullOrEmpty(t.url) && !string.Equals(t.url, url, StringComparison.Ordinal))
                        urlKeyMismatch.Add(new { fdbKey, dictKey = url, torrentUrl = t.url, title = t.title });

                    bool emptyMagnet = string.IsNullOrWhiteSpace(t.magnet);
                    bool emptyTypes = t.types == null || t.types.Length == 0;
                    if (emptyMagnet || emptyTypes)
                        emptyMagnetOrTypes.Add(new { fdbKey, url, title = t.title, emptyMagnet, emptyTypes });
                }
            }

            var orphanShardFiles = new IssueBucket(sampleSize);
            ScanOrphanShardFiles(expectedPaths, orphanShardFiles, ct);

            return new Dictionary<string, object>
            {
                ["totals"] = new { fdbKeys = masterKeys.Length, torrents = totalTorrents },
                ["issues"] = new
                {
                    nullValue = nullValue.ToObject(),
                    missingName = missingName.ToObject(),
                    missingOriginalname = missingOriginalname.ToObject(),
                    missingTrackerName = missingTrackerName.ToObject(),
                    emptySearchFields = new
                    {
                        emptySn = emptySn.ToObject(),
                        emptySo = emptySo.ToObject(),
                        emptyBoth = emptyBoth.ToObject(),
                        total = emptySn.Count + emptySo.Count + emptyBoth.Count
                    },
                    xxKeys = xxKeys.ToObject(),
                    bucketMismatch = bucketMismatch.ToObject(),
                    urlKeyMismatch = urlKeyMismatch.ToObject(),
                    missingShardFile = missingShardFile.ToObject(),
                    emptyShardListed = emptyShardListed.ToObject(),
                    orphanShardFiles = orphanShardFiles.ToObject(),
                    emptyMagnetOrTypes = emptyMagnetOrTypes.ToObject()
                }
            };
        }

        static void ScanOrphanShardFiles(HashSet<string> expectedPaths, IssueBucket orphans, CancellationToken ct)
        {
            if (!Directory.Exists("Data/fdb"))
                return;

            foreach (var file in Directory.EnumerateFiles("Data/fdb", "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;

                string full;
                try { full = Path.GetFullPath(file); }
                catch { full = file; }

                if (!expectedPaths.Contains(full))
                    orphans.Add(new { path = file });
            }
        }

        static bool IsXxKey(string key, bool excludeNumeric)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            int colon = key.IndexOf(':');
            if (colon <= 0 || colon >= key.Length - 1)
                return false;
            string part1 = key.Substring(0, colon);
            string part2 = key.Substring(colon + 1);
            if (!string.Equals(part1, part2, StringComparison.OrdinalIgnoreCase))
                return false;
            if (excludeNumeric && part1.Length > 0 && part1.All(char.IsDigit))
                return false;
            return true;
        }

        void ApplySafeFixes(FixedCounts fixedCounts, CancellationToken ct)
        {
            foreach (var item in FileDB.masterDb.ToArray())
            {
                ct.ThrowIfCancellationRequested();
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    var toMigrate = new List<(string url, TorrentDetails t, string newKey)>();
                    bool changed = false;

                    foreach (var torrent in fdb.Database.ToList())
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }

                        var t = torrent.Value;
                        bool fixedSn = false;
                        bool fixedSo = false;

                        if (string.IsNullOrWhiteSpace(t._sn))
                        {
                            if (!string.IsNullOrWhiteSpace(t.name))
                            {
                                t._sn = StringConvert.SearchName(t.name);
                                fixedSn = true;
                            }
                            else if (!string.IsNullOrWhiteSpace(t.title))
                            {
                                t._sn = StringConvert.SearchName(t.title);
                                fixedSn = true;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(t._so))
                        {
                            if (!string.IsNullOrWhiteSpace(t.originalname))
                            {
                                t._so = StringConvert.SearchName(t.originalname);
                                fixedSo = true;
                            }
                            else if (!string.IsNullOrWhiteSpace(t.name))
                            {
                                t._so = StringConvert.SearchName(t.name);
                                fixedSo = true;
                            }
                            else if (!string.IsNullOrWhiteSpace(t.title))
                            {
                                t._so = StringConvert.SearchName(t.title);
                                fixedSo = true;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(t.name))
                            t.name = t.title ?? "";
                        if (string.IsNullOrWhiteSpace(t.originalname))
                            t.originalname = t.name ?? t.title ?? "";

                        if (string.IsNullOrWhiteSpace(t._sn) && !string.IsNullOrWhiteSpace(t.name))
                        {
                            t._sn = StringConvert.SearchName(t.name);
                            fixedSn = true;
                        }
                        if (string.IsNullOrWhiteSpace(t._so) && !string.IsNullOrWhiteSpace(t.originalname))
                        {
                            t._so = StringConvert.SearchName(t.originalname);
                            fixedSo = true;
                        }

                        if (fixedSn || fixedSo)
                        {
                            fixedCounts.searchFieldsFixed++;
                            changed = true;
                            string newKey = FileDB.KeyForTorrent(t.name, t.originalname);
                            if (!string.IsNullOrEmpty(newKey) && newKey != item.Key && newKey.IndexOf(':') > 0)
                                toMigrate.Add((torrent.Key, t, newKey));
                        }
                    }

                    foreach (var k in keysToRemove)
                    {
                        fdb.Database.Remove(k);
                        fixedCounts.nullRemoved++;
                        changed = true;
                    }

                    foreach (var (url, t, newKey) in toMigrate)
                    {
                        fdb.Database.Remove(url);
                        FileDB.MigrateTorrentToNewKey(t, newKey);
                        fixedCounts.migrated++;
                        changed = true;
                    }

                    if (fdb.Database.Count == 0)
                    {
                        FileDB.RemoveKeyFromMasterDb(item.Key);
                        fixedCounts.emptyBucketsRemoved++;
                        changed = true;
                    }

                    if (changed)
                        fdb.savechanges = true;
                }
            }
        }

        void ApplyFullFixes(FixedCounts fixedCounts, CancellationToken ct)
        {
            // Remigrate bucket mismatches + url re-key + remove incomplete records
            foreach (var item in FileDB.masterDb.ToArray())
            {
                ct.ThrowIfCancellationRequested();
                string shardPath = FileDB.PathForKey(item.Key);
                if (!File.Exists(shardPath))
                {
                    FileDB.RemoveKeyFromMasterDb(item.Key);
                    fixedCounts.missingShardKeysRemoved++;
                    continue;
                }

                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    var toMigrate = new List<(string url, TorrentDetails t, string newKey)>();
                    var toRekey = new List<(string oldKey, string newUrl, TorrentDetails t)>();
                    bool changed = false;

                    foreach (var kv in fdb.Database.ToList())
                    {
                        if (kv.Value == null)
                        {
                            keysToRemove.Add(kv.Key);
                            continue;
                        }

                        var t = kv.Value;
                        bool emptyMagnet = string.IsNullOrWhiteSpace(t.magnet);
                        bool emptyTypes = t.types == null || t.types.Length == 0;
                        if (emptyMagnet && emptyTypes)
                        {
                            keysToRemove.Add(kv.Key);
                            fixedCounts.incompleteRemoved++;
                            continue;
                        }

                        string expectedKey = FileDB.KeyForTorrent(t.name, t.originalname);
                        if (!string.IsNullOrEmpty(expectedKey) && expectedKey != item.Key && expectedKey.IndexOf(':') > 0)
                        {
                            toMigrate.Add((kv.Key, t, expectedKey));
                            continue;
                        }

                        if (!string.IsNullOrEmpty(t.url) && !string.Equals(t.url, kv.Key, StringComparison.Ordinal))
                        {
                            if (fdb.Database.ContainsKey(t.url) && !string.Equals(t.url, kv.Key, StringComparison.Ordinal))
                            {
                                // Collision: drop the desynced dict entry
                                keysToRemove.Add(kv.Key);
                                fixedCounts.urlKeyFixed++;
                            }
                            else
                            {
                                toRekey.Add((kv.Key, t.url, t));
                            }
                        }
                    }

                    foreach (var k in keysToRemove)
                    {
                        fdb.Database.Remove(k);
                        changed = true;
                    }

                    foreach (var (oldKey, newUrl, t) in toRekey)
                    {
                        fdb.Database.Remove(oldKey);
                        if (!fdb.Database.ContainsKey(newUrl))
                            fdb.Database[newUrl] = t;
                        fixedCounts.urlKeyFixed++;
                        changed = true;
                    }

                    foreach (var (url, t, newKey) in toMigrate)
                    {
                        fdb.Database.Remove(url);
                        FileDB.MigrateTorrentToNewKey(t, newKey);
                        fixedCounts.migrated++;
                        changed = true;
                    }

                    if (fdb.Database.Count == 0)
                    {
                        FileDB.RemoveKeyFromMasterDb(item.Key);
                        fixedCounts.emptyBucketsRemoved++;
                        changed = true;
                    }

                    if (changed)
                        fdb.savechanges = true;
                }
            }

            // Delete orphan shard files on disk
            if (!Directory.Exists("Data/fdb"))
                return;

            var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in FileDB.masterDb.Keys)
            {
                try { expectedPaths.Add(Path.GetFullPath(FileDB.PathForKey(key))); }
                catch { expectedPaths.Add(FileDB.PathForKey(key)); }
            }

            foreach (var file in Directory.EnumerateFiles("Data/fdb", "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;

                string full;
                try { full = Path.GetFullPath(file); }
                catch { full = file; }

                if (expectedPaths.Contains(full))
                    continue;

                try
                {
                    File.Delete(file);
                    fixedCounts.orphansDeleted++;
                }
                catch (Exception ex)
                {
                    JacRedLog.Warning(JacRedLogCategories.Fdb,
                        $"maintenance: failed to delete orphan {file}: {ex.Message}");
                }
            }
        }

        void WriteReport(object report)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine("Data", "temp"));
                File.WriteAllText(ReportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            catch (Exception ex)
            {
                JacRedLog.Warning(JacRedLogCategories.Fdb,
                    $"maintenance: failed to write {ReportPath}: {ex.Message}");
            }
        }

        void TryLoadLastReport()
        {
            try
            {
                if (!File.Exists(ReportPath))
                    return;
                _lastReport = JsonConvert.DeserializeObject(File.ReadAllText(ReportPath));
            }
            catch { }
        }

        sealed class IssueBucket
        {
            readonly int _sampleSize;
            readonly List<object> _sample = new List<object>();

            public IssueBucket(int sampleSize) => _sampleSize = sampleSize;

            public int Count { get; private set; }

            public void Add(object sample)
            {
                Count++;
                if (_sample.Count < _sampleSize)
                    _sample.Add(sample);
            }

            public object ToObject() => new { count = Count, sample = _sample };
        }

        sealed class FixedCounts
        {
            public int nullRemoved;
            public int searchFieldsFixed;
            public int migrated;
            public int emptyBucketsRemoved;
            public int missingShardKeysRemoved;
            public int orphansDeleted;
            public int urlKeyFixed;
            public int incompleteRemoved;

            public object ToObject() => new
            {
                nullRemoved,
                searchFieldsFixed,
                migrated,
                emptyBucketsRemoved,
                missingShardKeysRemoved,
                orphansDeleted,
                urlKeyFixed,
                incompleteRemoved
            };
        }
    }
}
