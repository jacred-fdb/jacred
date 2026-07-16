using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using JacRed.Infrastructure.Utils;
using JacRed.Models;
using JacRed.Models.Details;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Persistence
{
    public partial class FileDB : IDisposable
    {
        #region FileDB
        string fdbkey;

        public bool savechanges = false;

        readonly System.Threading.ReaderWriterLockSlim _dbLock = new System.Threading.ReaderWriterLockSlim();
        Dictionary<string, TorrentDetails> _readSnapshot;
        bool _snapshotDirty = true;

        /// <summary>trackerName|torrentId → url for O(1) upsert by id.</summary>
        readonly Dictionary<string, string> _torrentIdIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        FileDB(string key)
        {
            fdbkey = key;
            string fdbpath = pathDb(key);

            if (File.Exists(fdbpath))
                Database = JsonStream.Read<Dictionary<string, TorrentDetails>>(fdbpath) ?? new Dictionary<string, TorrentDetails>();

            RebuildTorrentIdIndex();
        }

        public Dictionary<string, TorrentDetails> Database = new Dictionary<string, TorrentDetails>();

        void RebuildTorrentIdIndex()
        {
            _torrentIdIndex.Clear();
            foreach (var kv in Database)
            {
                if (kv.Value == null || string.IsNullOrEmpty(kv.Value.trackerName))
                    continue;
                int id = GetTorrentIdFromUrl(kv.Value.trackerName, kv.Key);
                if (id <= 0)
                    continue;
                _torrentIdIndex[$"{kv.Value.trackerName}|{id}"] = kv.Key;
            }
        }

        void IndexTorrentUrl(string trackerName, string url)
        {
            if (string.IsNullOrEmpty(trackerName) || string.IsNullOrEmpty(url))
                return;
            int id = GetTorrentIdFromUrl(trackerName, url);
            if (id <= 0)
                return;
            _torrentIdIndex[$"{trackerName}|{id}"] = url;
        }

        void UnindexTorrentUrl(string trackerName, string url)
        {
            if (string.IsNullOrEmpty(trackerName) || string.IsNullOrEmpty(url))
                return;
            int id = GetTorrentIdFromUrl(trackerName, url);
            if (id <= 0)
                return;
            string key = $"{trackerName}|{id}";
            if (_torrentIdIndex.TryGetValue(key, out string indexed) && string.Equals(indexed, url, StringComparison.OrdinalIgnoreCase))
                _torrentIdIndex.Remove(key);
        }

        /// <summary>
        /// Returns a frozen dictionary copy. Readers share the same snapshot until the next write
        /// (avoids reallocating on every OpenRead of an unchanged shard).
        /// </summary>
        internal IReadOnlyDictionary<string, TorrentDetails> GetSnapshot()
        {
            _dbLock.EnterReadLock();
            try
            {
                if (!_snapshotDirty && _readSnapshot != null)
                    return _readSnapshot;
            }
            finally
            {
                _dbLock.ExitReadLock();
            }

            _dbLock.EnterWriteLock();
            try
            {
                if (!_snapshotDirty && _readSnapshot != null)
                    return _readSnapshot;

                _readSnapshot = new Dictionary<string, TorrentDetails>(Database);
                _snapshotDirty = false;
                return _readSnapshot;
            }
            finally
            {
                _dbLock.ExitWriteLock();
            }
        }

        void MarkSnapshotDirty()
        {
            _snapshotDirty = true;
            _readSnapshot = null;
        }

        internal void SaveChangesIfNeeded()
        {
            _dbLock.EnterWriteLock();
            try
            {
                if (Database.Count > 0 && savechanges)
                    JsonStream.Write(pathDb(fdbkey), Database);
            }
            finally
            {
                _dbLock.ExitWriteLock();
            }
        }
        #endregion

        #region AddOrUpdate

        public void AddOrUpdate(TorrentBaseDetails torrent)
        {
            _dbLock.EnterWriteLock();
            try
            {
                AddOrUpdateCore(torrent);
            }
            finally
            {
                _dbLock.ExitWriteLock();
            }
        }

        void AddOrUpdateCore(TorrentBaseDetails torrent)
        {
            bool foundById = false;
            if (!Database.TryGetValue(torrent.url, out TorrentDetails t))
            {
                int torrentId = GetTorrentIdFromUrl(torrent.trackerName, torrent.url);
                if (torrentId > 0
                    && _torrentIdIndex.TryGetValue($"{torrent.trackerName}|{torrentId}", out string existingUrl)
                    && !string.Equals(existingUrl, torrent.url, StringComparison.OrdinalIgnoreCase)
                    && Database.TryGetValue(existingUrl, out t))
                {
                    Database.Remove(existingUrl);
                    UnindexTorrentUrl(torrent.trackerName, existingUrl);
                    t.url = torrent.url;
                    foundById = true;
                    MarkSnapshotDirty();
                }
            }

            if (t != null)
            {
                bool updateFull = false;

                void upt(bool uptfull = false, bool updatetime = true)
                {
                    savechanges = true;
                    MarkSnapshotDirty();

                    if (updatetime)
                    {
                        t.updateTime = DateTime.UtcNow;
                        t.ffprobe_tryingdata = 0;
                    }

                    if (uptfull)
                        updateFull = true;
                }

                #region types
                if (torrent.types != null)
                {
                    if (t.types == null)
                    {
                        t.types = torrent.types;
                        upt(true);
                    }
                    else
                    {
                        foreach (string type in torrent.types)
                        {
                            if (type != null && !t.types.Contains(type))
                                upt(true);
                        }

                        t.types = torrent.types;
                    }
                }
                #endregion

                if (torrent.trackerName != t.trackerName)
                {
                    t.trackerName = torrent.trackerName;
                    upt(true);
                }

                if (torrent.title != t.title)
                {
                    t.title = torrent.title;
                    upt(true);
                }

                if (torrent.createTime != default && torrent.createTime > t.createTime)
                {
                    t.createTime = torrent.createTime;
                    upt(updatetime: false);
                }

                if (!string.IsNullOrWhiteSpace(torrent.magnet) && torrent.magnet != t.magnet)
                {
                    t.ffprobe_tryingdata = 0;
                    t.ffprobe = null;
                    t.magnet = torrent.magnet;
                    upt();
                }

                if (torrent.sid != t.sid)
                {
                    if (t.sid == 0 && torrent.sid >= 2 && t.ffprobe_tryingdata >= AppInit.conf.tracksatempt)
                    {
                        t.ffprobe_tryingdata = 0;
                    }
                    t.sid = torrent.sid;
                    upt(updatetime: false);
                }

                if (torrent.pir != t.pir)
                {
                    t.pir = torrent.pir;
                    upt(updatetime: false);
                }

                if (!string.IsNullOrWhiteSpace(torrent.sizeName) && torrent.sizeName != t.sizeName)
                {
                    t.sizeName = torrent.sizeName;
                    upt(true);
                }

                if (!string.IsNullOrWhiteSpace(torrent.name) && torrent.name != t.name)
                {
                    t.name = torrent.name;
                    t._sn = StringConvert.SearchName(t.name);
                    upt();
                }
                else if (string.IsNullOrWhiteSpace(t.name) && !string.IsNullOrWhiteSpace(torrent.title))
                {
                    t.name = torrent.title;
                    t._sn = StringConvert.SearchName(t.name);
                    upt();
                }
                // Убеждаемся, что _sn всегда заполнен, даже если name не изменился
                if (string.IsNullOrWhiteSpace(t._sn))
                {
                    if (!string.IsNullOrWhiteSpace(t.name))
                        t._sn = StringConvert.SearchName(t.name);
                    else if (!string.IsNullOrWhiteSpace(torrent.title))
                        t._sn = StringConvert.SearchName(torrent.title);

                    if (!string.IsNullOrWhiteSpace(t._sn))
                        upt();
                }

                if (!string.IsNullOrWhiteSpace(torrent.originalname) && torrent.originalname != t.originalname)
                {
                    t.originalname = torrent.originalname;
                    t._so = StringConvert.SearchName(t.originalname);
                    upt();
                }
                else if (string.IsNullOrWhiteSpace(t.originalname))
                {
                    // For Russian content where originalname is null, use name instead of title
                    // to avoid creating keys with full title (including season/episode info)
                    t.originalname = !string.IsNullOrWhiteSpace(t.name) ? t.name : (torrent.title ?? "");
                    t._so = StringConvert.SearchName(t.originalname);
                    upt();
                }
                // Убеждаемся, что _so всегда заполнен, даже если originalname не изменился
                if (string.IsNullOrWhiteSpace(t._so))
                {
                    if (!string.IsNullOrWhiteSpace(t.originalname))
                        t._so = StringConvert.SearchName(t.originalname);
                    else if (!string.IsNullOrWhiteSpace(t.name))
                        t._so = StringConvert.SearchName(t.name);
                    else if (!string.IsNullOrWhiteSpace(torrent.title))
                        t._so = StringConvert.SearchName(torrent.title);

                    if (!string.IsNullOrWhiteSpace(t._so))
                        upt();
                }

                if (torrent.relased > 0 && torrent.relased != t.relased)
                {
                    t.relased = torrent.relased;
                    upt();
                }

                if (torrent.ffprobe != null && t.ffprobe == null)
                {
                    t.ffprobe = torrent.ffprobe;
                    upt();
                }

                if (updateFull)
                {
                    updateFullDetails(t);
                    if (AppInit.conf.logFdb)
                        AppendFdbLog(torrent, t);
                }
                else if (AppInit.conf.logFdb)
                    AppendFdbLog(torrent, t);

                t.checkTime = DateTime.Now;

                if (foundById)
                {
                    Database.TryAdd(t.url, t);
                    IndexTorrentUrl(t.trackerName, t.url);
                    MarkSnapshotDirty();
                }

                // Drop legacy bare episode/movie URL once a #quality row is stored.
                if (string.Equals(t.trackerName, "lostfilm", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(t.url) && t.url.Contains('#', StringComparison.Ordinal))
                {
                    string bare = t.url.Substring(0, t.url.IndexOf('#'));
                    if (!string.IsNullOrEmpty(bare) && Database.ContainsKey(bare))
                    {
                        UnindexTorrentUrl(t.trackerName, bare);
                        Database.Remove(bare);
                        savechanges = true;
                        MarkSnapshotDirty();
                    }
                }

                if (string.Equals(t.trackerName, "lostfilm", StringComparison.OrdinalIgnoreCase))
                {
                    string newKey = keyDb(t.name, t.originalname);
                    if (!string.IsNullOrEmpty(newKey) && newKey != fdbkey && newKey.IndexOf(':') > 0)
                    {
                        Database.Remove(t.url);
                        savechanges = true;
                        MigrateTorrentToNewKey(t, newKey);
                        if (Database.Count == 0)
                            RemoveKeyFromMasterDb(fdbkey);
                        return;
                    }
                }
                AddOrUpdateMasterDb(t);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(torrent.magnet) || torrent.types == null || torrent.types.Length == 0)
                    return;

                var name = torrent.name ?? torrent.title ?? "";
                // For Russian content where originalname is null, use name instead of title
                // to avoid creating keys with full title (including season/episode info)
                var originalname = torrent.originalname ?? name ?? "";

                // Убеждаемся, что name и originalname не пустые
                if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(torrent.title))
                    name = torrent.title;
                if (string.IsNullOrWhiteSpace(originalname))
                    originalname = name ?? torrent.title ?? "";

                t = new TorrentDetails()
                {
                    url = torrent.url,
                    types = torrent.types,
                    trackerName = torrent.trackerName,
                    createTime = torrent.createTime,
                    updateTime = torrent.updateTime,
                    title = torrent.title,
                    name = name,
                    originalname = originalname,
                    pir = torrent.pir,
                    sid = torrent.sid,
                    relased = torrent.relased,
                    sizeName = torrent.sizeName,
                    magnet = torrent.magnet,
                    ffprobe = torrent.ffprobe
                };

                // Всегда заполняем _sn и _so, даже если name или originalname пустые
                // Используем fallback на title если нужно
                t._sn = StringConvert.SearchName(t.name);
                if (string.IsNullOrWhiteSpace(t._sn) && !string.IsNullOrWhiteSpace(t.title))
                    t._sn = StringConvert.SearchName(t.title);

                t._so = StringConvert.SearchName(t.originalname);
                if (string.IsNullOrWhiteSpace(t._so))
                {
                    // Если originalname пустое, используем name или title
                    if (!string.IsNullOrWhiteSpace(t.name))
                        t._so = StringConvert.SearchName(t.name);
                    else if (!string.IsNullOrWhiteSpace(t.title))
                        t._so = StringConvert.SearchName(t.title);
                }

                savechanges = true;
                updateFullDetails(t);

                if (AppInit.conf.logFdb)
                    AppendFdbLog(torrent, t);

                Database.TryAdd(t.url, t);
                IndexTorrentUrl(t.trackerName, t.url);
                MarkSnapshotDirty();
                AddOrUpdateMasterDb(t);

                // Drop legacy bare episode/movie URL once a #quality row is stored.
                if (string.Equals(t.trackerName, "lostfilm", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(t.url) && t.url.Contains('#', StringComparison.Ordinal))
                {
                    string bare = t.url.Substring(0, t.url.IndexOf('#'));
                    if (!string.IsNullOrEmpty(bare) && Database.ContainsKey(bare))
                    {
                        UnindexTorrentUrl(t.trackerName, bare);
                        Database.Remove(bare);
                        savechanges = true;
                        MarkSnapshotDirty();
                    }
                }
            }
        }
        #endregion

        #region FdbLog
        static readonly string FdbLogDir = "Data/log";
        const string FdbLogPrefix = "fdb.";
        static readonly System.Collections.Concurrent.ConcurrentQueue<string> FdbLogQueue =
            new System.Collections.Concurrent.ConcurrentQueue<string>();
        static int _fdbLogFlushScheduled;

        static void AppendFdbLog(TorrentBaseDetails torrent, TorrentDetails t)
        {
            try
            {
                string jsonLine = JsonConvert.SerializeObject(new List<TorrentBaseDetails>() { torrent, t }, Formatting.None) + "\n";
                FdbLogQueue.Enqueue(jsonLine);
                if (System.Threading.Interlocked.CompareExchange(ref _fdbLogFlushScheduled, 1, 0) == 0)
                    _ = System.Threading.Tasks.Task.Run(FlushFdbLogQueue);
            }
            catch { }
        }

        static void FlushFdbLogQueue()
        {
            try
            {
                if (!Directory.Exists(FdbLogDir))
                    Directory.CreateDirectory(FdbLogDir);

                int retentionDays = AppInit.conf?.logFdbRetentionDays ?? 0;
                if (retentionDays > 0)
                {
                    var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
                    foreach (var path in Directory.EnumerateFiles(FdbLogDir, FdbLogPrefix + "*.log"))
                    {
                        string name = Path.GetFileNameWithoutExtension(path);
                        if (name.Length > FdbLogPrefix.Length && DateTime.TryParseExact(name.Substring(FdbLogPrefix.Length), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate) && fileDate < cutoff)
                            try { File.Delete(path); } catch { }
                    }
                }

                string logPath = Path.Combine(FdbLogDir, FdbLogPrefix + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log");
                var sb = new System.Text.StringBuilder();
                while (FdbLogQueue.TryDequeue(out string line))
                    sb.Append(line);

                if (sb.Length > 0)
                    File.AppendAllText(logPath, sb.ToString());

                PurgeFdbLogBySizeAndCount();
            }
            catch { }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _fdbLogFlushScheduled, 0);
                if (!FdbLogQueue.IsEmpty
                    && System.Threading.Interlocked.CompareExchange(ref _fdbLogFlushScheduled, 1, 0) == 0)
                    _ = System.Threading.Tasks.Task.Run(FlushFdbLogQueue);
            }
        }

        static void PurgeFdbLogBySizeAndCount()
        {
            int maxSizeMb = AppInit.conf?.logFdbMaxSizeMb ?? 0;
            int maxFiles = AppInit.conf?.logFdbMaxFiles ?? 0;
            if (maxSizeMb <= 0 && maxFiles <= 0)
                return;
            try
            {
                long maxBytes = maxSizeMb > 0 ? (long)maxSizeMb * 1024 * 1024 : long.MaxValue;
                var list = new List<(string path, long length, DateTime date)>();
                foreach (var path in Directory.EnumerateFiles(FdbLogDir, FdbLogPrefix + "*.log"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.Length <= FdbLogPrefix.Length || !DateTime.TryParseExact(name.Substring(FdbLogPrefix.Length), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate))
                        continue;
                    long len = 0;
                    try { len = new FileInfo(path).Length; } catch { }
                    list.Add((path, len, fileDate));
                }
                list.Sort((a, b) => a.date.CompareTo(b.date));
                long total = list.Sum(x => x.length);
                int count = list.Count;
                foreach (var item in list)
                {
                    if ((maxSizeMb <= 0 || total <= maxBytes) && (maxFiles <= 0 || count <= maxFiles))
                        break;
                    try
                    {
                        File.Delete(item.path);
                        total -= item.length;
                        count--;
                    }
                    catch { }
                }
            }
            catch { }
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            SaveChangesIfNeeded();

            if (openWriteTask.TryGetValue(fdbkey, out WriteTaskModel val))
            {
                if (System.Threading.Interlocked.Decrement(ref val.openconnection) <= 0)
                {
                    if (!AppInit.conf.evercache.enable || (AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour > 0))
                        openWriteTask.TryRemove(fdbkey, out _);
                }
            }
        }
        #endregion


    }
}
