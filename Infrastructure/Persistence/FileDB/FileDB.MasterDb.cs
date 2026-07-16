using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Infrastructure.Utils;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Logging;
using JacRed.Models;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Persistence
{
    public partial class FileDB
    {
        #region FileDB
        /// <summary>
        /// $"{search_name}:{search_originalname}"
        /// Верхнее время изменения
        /// </summary>
        public static ConcurrentDictionary<string, MasterDbShard> masterDb = new ConcurrentDictionary<string, MasterDbShard>();

        static ConcurrentDictionary<string, WriteTaskModel> openWriteTask = new ConcurrentDictionary<string, WriteTaskModel>();

        static FileDB()
        {
            if (File.Exists("Data/masterDb.bz"))
                masterDb = JsonStream.Read<ConcurrentDictionary<string, MasterDbShard>>("Data/masterDb.bz");

            if (masterDb == null)
            {
                if (File.Exists($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz"))
                    masterDb = JsonStream.Read<ConcurrentDictionary<string, MasterDbShard>>($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz");

                if (masterDb == null && File.Exists($"Data/masterDb_{DateTime.Today.AddDays(-1):dd-MM-yyyy}.bz"))
                    masterDb = JsonStream.Read<ConcurrentDictionary<string, MasterDbShard>>($"Data/masterDb_{DateTime.Today.AddDays(-1):dd-MM-yyyy}.bz");

                if (masterDb == null)
                    masterDb = new ConcurrentDictionary<string, MasterDbShard>();

                #region переход с 29.08.2023
                if (File.Exists("Data/masterDb.bz"))
                {
                    try
                    {
                        foreach (var item in JsonStream.Read<Dictionary<string, DateTime>>("Data/masterDb.bz"))
                        {
                            masterDb.TryAdd(item.Key, new MasterDbShard
                            {
                                updateTime = item.Value,
                                fileTime = item.Value.ToFileTimeUtc()
                            });
                        }

                        if (masterDb.Count > 0)
                        {
                            JsonStream.Write("Data/masterDb.bz", masterDb);
                            return;
                        }
                    }
                    catch { }
                }
                #endregion

                if (File.Exists(Path.Combine("Data", "temp", "lastsync.txt")))
                    File.Delete(Path.Combine("Data", "temp", "lastsync.txt"));
            }
        }
        #endregion

        #region pathDb / keyDb
        static string pathDb(string key)
        {
            string md5key = HashTo.md5(key);

            if (AppInit.conf.fdbPathLevels == 2)
            {
                Directory.CreateDirectory($"Data/fdb/{md5key.Substring(0, 2)}");
                return $"Data/fdb/{md5key.Substring(0, 2)}/{md5key.Substring(2)}";
            }
            else
            {
                Directory.CreateDirectory($"Data/fdb/{md5key[0]}");
                return $"Data/fdb/{md5key[0]}/{md5key}";
            }
        }

        static string keyDb(string name, string originalname)
        {
            string search_name = StringConvert.SearchName(name);
            string search_originalname = StringConvert.SearchName(originalname);

            // Если search_name или search_originalname null, используем fallback
            // Это важно для случаев, когда name или originalname пустые после нормализации
            if (string.IsNullOrWhiteSpace(search_name))
            {
                // Пробуем использовать originalname если name пустое
                if (!string.IsNullOrWhiteSpace(search_originalname))
                    search_name = search_originalname;
                else
                    // Если оба пустые, используем пустую строку вместо null
                    search_name = "";
            }

            if (string.IsNullOrWhiteSpace(search_originalname))
            {
                // Пробуем использовать name если originalname пустое
                if (!string.IsNullOrWhiteSpace(search_name))
                    search_originalname = search_name;
                else
                    // Если оба пустые, используем пустую строку вместо null
                    search_originalname = "";
            }

            return $"{search_name}:{search_originalname}";
        }

        /// <summary>Ключ бакета по name/originalname (для поиска и миграции).</summary>
        public static string KeyForTorrent(string name, string originalname) => keyDb(name, originalname);

        #endregion

        /// <summary>Перенос торрента в бакет с ключом newKey (после смены name/originalname). Вызывается из FileDB и из DevMaintenanceService.UpdateSearchName.</summary>
        public static void MigrateTorrentToNewKey(TorrentDetails t, string newKey)
        {
            using (var fdb = OpenWrite(newKey))
            {
                fdb.AddOrUpdate(t);
            }
        }

        /// <summary>Удаляет ключ из masterDb (например после миграции, когда бакет опустел). Вызывать только если бакет действительно пуст.</summary>
        public static void RemoveKeyFromMasterDb(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            masterDb.TryRemove(key, out _);
        }

        #region AddOrUpdateMasterDb
        static void AddOrUpdateMasterDb(TorrentDetails torrent)
        {
            string key = keyDb(torrent.name, torrent.originalname);
            var md = new MasterDbShard() { updateTime = torrent.updateTime, fileTime = torrent.updateTime.ToFileTimeUtc() };

            if (masterDb.TryGetValue(key, out MasterDbShard info))
            {
                if (torrent.updateTime > info.updateTime)
                    masterDb[key] = md;
            }
            else
            {
                masterDb.TryAdd(key, md);
            }
        }
        #endregion

        #region OpenRead / OpenWrite
        static readonly System.Threading.AsyncLocal<Dictionary<string, IReadOnlyDictionary<string, TorrentDetails>>> RequestReadCache =
            new System.Threading.AsyncLocal<Dictionary<string, IReadOnlyDictionary<string, TorrentDetails>>>();

        /// <summary>Deduplicate OpenRead within a single search/HTTP request.</summary>
        public static void BeginRequestReadCache()
            => RequestReadCache.Value = new Dictionary<string, IReadOnlyDictionary<string, TorrentDetails>>(StringComparer.Ordinal);

        public static void EndRequestReadCache()
            => RequestReadCache.Value = null;

        /// <summary>Always returns a snapshot — never expose live Database to concurrent readers.</summary>
        public static IReadOnlyDictionary<string, TorrentDetails> OpenRead(string key, bool update_lastread = false, bool cache = true)
        {
            var reqCache = RequestReadCache.Value;
            if (reqCache != null && reqCache.TryGetValue(key, out var cached))
            {
                if (update_lastread && openWriteTask.TryGetValue(key, out WriteTaskModel cachedWtm))
                {
                    cachedWtm.countread++;
                    cachedWtm.lastread = DateTime.UtcNow;
                }
                return cached;
            }

            IReadOnlyDictionary<string, TorrentDetails> snapshot;

            if (openWriteTask.TryGetValue(key, out WriteTaskModel val))
            {
                if (update_lastread)
                {
                    val.countread++;
                    val.lastread = DateTime.UtcNow;
                }

                snapshot = val.db.GetSnapshot();
                reqCache?.TryAdd(key, snapshot);
                return snapshot;
            }

            var fdb = new FileDB(key);

            if (AppInit.conf.evercache.enable && (cache || AppInit.conf.evercache.validHour == 0))
            {
                var wtm = new WriteTaskModel() { db = fdb, openconnection = 0 };
                if (update_lastread)
                {
                    wtm.countread++;
                    wtm.lastread = DateTime.UtcNow;
                }

                if (openWriteTask.TryAdd(key, wtm))
                {
                    snapshot = fdb.GetSnapshot();
                    reqCache?.TryAdd(key, snapshot);
                    return snapshot;
                }

                fdb.Dispose();
                if (openWriteTask.TryGetValue(key, out val))
                {
                    if (update_lastread)
                    {
                        val.countread++;
                        val.lastread = DateTime.UtcNow;
                    }
                    snapshot = val.db.GetSnapshot();
                    reqCache?.TryAdd(key, snapshot);
                    return snapshot;
                }
            }

            snapshot = fdb.GetSnapshot();
            fdb.Dispose();
            reqCache?.TryAdd(key, snapshot);
            return snapshot;
        }

        public static FileDB OpenWrite(string key)
        {
            while (true)
            {
                if (openWriteTask.TryGetValue(key, out WriteTaskModel val))
                {
                    System.Threading.Interlocked.Increment(ref val.openconnection);
                    return val.db;
                }

                var fdb = new FileDB(key);
                var wtm = new WriteTaskModel() { db = fdb, openconnection = 1 };
                if (openWriteTask.TryAdd(key, wtm))
                    return fdb;

                // Lost race — another writer added the same key.
                fdb.Dispose();
            }
        }
        #endregion

        #region AddOrUpdate
        public static void AddOrUpdate(IReadOnlyCollection<TorrentBaseDetails> torrents)
        {
            _ = AddOrUpdate(torrents, null);
        }

        async public static ValueTask AddOrUpdate<T>(IReadOnlyCollection<T> torrents, Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate) where T : TorrentBaseDetails
        {
            var temp = new Dictionary<string, List<T>>();

            foreach (var torrent in torrents)
            {
                string key = keyDb(torrent.name, torrent.originalname);
                if (!temp.ContainsKey(key))
                    temp.Add(key, new List<T>());

                temp[key].Add(torrent);
            }

            foreach (var t in temp)
            {
                using (var fdb = OpenWrite(t.Key))
                {
                    foreach (var torrent in t.Value)
                    {
                        if (predicate != null)
                        {
                            if (await predicate.Invoke(torrent, fdb.Database) == false)
                                continue;
                        }

                        fdb.AddOrUpdate(torrent);
                    }
                }
            }
        }
        #endregion

        #region SaveChangesToFile
        public static void SaveChangesToFile()
        {
            try
            {
                JsonStream.Write("Data/masterDb.bz", masterDb);

                if (!File.Exists($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz"))
                    File.Copy("Data/masterDb.bz", $"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz");

                if (File.Exists($"Data/masterDb_{DateTime.Today.AddDays(-3):dd-MM-yyyy}.bz"))
                    File.Delete($"Data/masterDb_{DateTime.Today.AddDays(-3):dd-MM-yyyy}.bz");
            }
            catch { }
        }
        #endregion

        ///by Lexandros
        /// <summary>
        /// Обновляет информацию о попытках анализа ffprobe для торрента
        /// </summary>
        /// <param name="torrentKey">Ключ торрента в базе (search_name:search_originalname)</param>
        /// <param name="magnet">Magnet-ссылка торрента для поиска</param>
        /// <param name="ffprobeTryingData">Новое значение счетчика попыток</param>
        /// <param name="ffprobeResult">Результаты анализа ffprobe (опционально)</param>
        public static void UpdateTorrentFfprobeInfo(string torrentKey, string magnet, int ffprobeTryingData, JacRed.Models.Tracks.FfprobeModel ffprobeResult = null)
        {
            if (string.IsNullOrEmpty(torrentKey) || string.IsNullOrEmpty(magnet))
                return;

            try
            {
                using (var fdb = OpenWrite(torrentKey))
                {
                    // Ищем торрент по magnet ссылке
                    var torrent = fdb.Database.Values.FirstOrDefault(t =>
                        !string.IsNullOrEmpty(t.magnet) &&
                        t.magnet.Equals(magnet, StringComparison.OrdinalIgnoreCase));

                    if (torrent != null)
                    {
                        bool updated = false;

                        // Обновляем счетчик попыток
                        if (torrent.ffprobe_tryingdata != ffprobeTryingData)
                        {
                            torrent.ffprobe_tryingdata = ffprobeTryingData;
                            updated = true;
                        }

                        // Обновляем результаты анализа (если есть)
                        if (ffprobeResult != null && ffprobeResult.streams != null && ffprobeResult.streams.Count > 0)
                        {
                            torrent.ffprobe = ffprobeResult.streams;  // Преобразуем FfprobeModel в List<ffStream>
                            updated = true;
                        }

                        if (updated)
                        {
                            // Обновляем время изменения
                            torrent.updateTime = DateTime.UtcNow;

                            // Помечаем для сохранения при Dispose
                            fdb.savechanges = true;

                            // Обновляем masterDb
                            AddOrUpdateMasterDb(torrent);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Fdb, $"Ошибка при обновлении ffprobe информации: {ex.Message}");
            }
        }


    }
}
