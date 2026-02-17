using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using JacRed.Engine.CORE;
using JacRed.Models;
using JacRed.Models.Details;
using System.Linq;

namespace JacRed.Engine
{
    public partial class FileDB : IDisposable
    {
        #region FileDB
        /// <summary>
        /// $"{search_name}:{search_originalname}"
        /// Верхнее время изменения
        /// </summary>
        public static ConcurrentDictionary<string, TorrentInfo> masterDb = new ConcurrentDictionary<string, TorrentInfo>();

        static ConcurrentDictionary<string, WriteTaskModel> openWriteTask = new ConcurrentDictionary<string, WriteTaskModel>();

        static FileDB()
        {
            if (File.Exists("Data/masterDb.bz"))
                masterDb = JsonStream.Read<ConcurrentDictionary<string, TorrentInfo>>("Data/masterDb.bz");

            if (masterDb == null)
            {
                if (File.Exists($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz"))
                    masterDb = JsonStream.Read<ConcurrentDictionary<string, TorrentInfo>>($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz");

                if (masterDb == null && File.Exists($"Data/masterDb_{DateTime.Today.AddDays(-1):dd-MM-yyyy}.bz"))
                    masterDb = JsonStream.Read<ConcurrentDictionary<string, TorrentInfo>>($"Data/masterDb_{DateTime.Today.AddDays(-1):dd-MM-yyyy}.bz");

                if (masterDb == null)
                    masterDb = new ConcurrentDictionary<string, TorrentInfo>();

                #region переход с 29.08.2023
                if (File.Exists("Data/masterDb.bz"))
                {
                    try
                    {
                        foreach (var item in JsonStream.Read<Dictionary<string, DateTime>>("Data/masterDb.bz"))
                        {
                            masterDb.TryAdd(item.Key, new TorrentInfo
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

        /// <summary>Перенос торрента в бакет с ключом newKey (после смены name/originalname). Вызывается из FileDB и из DevController.UpdateSearchName.</summary>
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
            var md = new TorrentInfo() { updateTime = torrent.updateTime, fileTime = torrent.updateTime.ToFileTimeUtc() };

            if (masterDb.TryGetValue(key, out TorrentInfo info))
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
        public static IReadOnlyDictionary<string, TorrentDetails> OpenRead(string key, bool update_lastread = false, bool cache = true)
        {
            if (openWriteTask.TryGetValue(key, out WriteTaskModel val))
            {
                if (update_lastread)
                {
                    val.countread++;
                    val.lastread = DateTime.UtcNow;
                }

                return val.db.Database;
            }

            var fdb = new FileDB(key);

            if (AppInit.conf.evercache.enable && (cache || AppInit.conf.evercache.validHour == 0))
            {
                var wtm = new WriteTaskModel() { db = fdb, openconnection = 1 };
                if (update_lastread)
                {
                    wtm.countread++;
                    wtm.lastread = DateTime.UtcNow;
                }

                openWriteTask.TryAdd(key, wtm);
            }

            return fdb.Database;
        }

        public static FileDB OpenWrite(string key)
        {
            if (openWriteTask.TryGetValue(key, out WriteTaskModel val))
            {
                val.openconnection += 1;
                return val.db;
            }
            else
            {
                var fdb = new FileDB(key);
                openWriteTask.TryAdd(key, new WriteTaskModel() { db = fdb, openconnection = 1 });
                return fdb;
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


        #region Cron
        async public static Task Cron()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(10));

                if (!AppInit.conf.evercache.enable || 0 >= AppInit.conf.evercache.validHour)
                    continue;

                try
                {
                    int evicted = 0;
                    foreach (var i in openWriteTask)
                    {
                        if (DateTime.UtcNow > i.Value.lastread.AddHours(AppInit.conf.evercache.validHour))
                        {
                            if (openWriteTask.TryRemove(i.Key, out _))
                                evicted++;
                        }
                    }
                    if (evicted > 0)
                        Console.WriteLine($"fdb_cron: evicted {evicted} cache entries (validHour) / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                catch { }
            }
        }

        async public static Task CronFast()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(20));

                if (!AppInit.conf.evercache.enable || 0 >= AppInit.conf.evercache.validHour)
                    continue;

                try
                {
                    if (openWriteTask.Count > AppInit.conf.evercache.maxOpenWriteTask)
                    {
                        var query = openWriteTask.Where(i => DateTime.Now > i.Value.create.AddMinutes(10));
                        query = query.OrderBy(i => i.Value.countread).ThenBy(i => i.Value.lastread);

                        int dropped = 0;
                        foreach (var i in query.Take(AppInit.conf.evercache.dropCacheTake))
                        {
                            if (openWriteTask.TryRemove(i.Key, out _))
                                dropped++;
                        }
                        if (dropped > 0)
                            Console.WriteLine($"fdb_cron_fast: dropped {dropped} cache entries (maxOpenWriteTask) / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    }
                }
                catch { }
            }
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
                Console.WriteLine($"Ошибка при обновлении ffprobe информации: {ex.Message}");
            }
        }

        ///

    }
}
