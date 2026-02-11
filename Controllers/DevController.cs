using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Engine;
using JacRed.Engine.CORE;
using JacRed.Models.Details;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers
{
    [Route("/dev/[action]")]
    public class DevController : Controller
    {
        public JsonResult UpdateSize()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            #region getSizeInfo
            long getSizeInfo(string sizeName)
            {
                if (string.IsNullOrWhiteSpace(sizeName))
                    return 0;

                try
                {
                    double size = 0.1;
                    var gsize = Regex.Match(sizeName, "([0-9\\.,]+) (Mb|МБ|GB|ГБ|TB|ТБ)", RegexOptions.IgnoreCase).Groups;
                    if (!string.IsNullOrWhiteSpace(gsize[2].Value))
                    {
                        if (double.TryParse(gsize[1].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out size) && size != 0)
                        {
                            if (gsize[2].Value.ToLower() is "gb" or "гб")
                                size *= 1024;

                            if (gsize[2].Value.ToLower() is "tb" or "тб")
                                size *= 1048576;

                            return (long)(size * 1048576);
                        }
                    }
                }
                catch { }

                return 0;
            }
            #endregion

            foreach (var item in FileDB.masterDb.OrderBy(i => i.Value.fileTime).ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }
                        torrent.Value.size = getSizeInfo(torrent.Value.sizeName);
                        torrent.Value.updateTime = DateTime.UtcNow;
                        FileDB.masterDb[item.Key] = new Models.TorrentInfo() { updateTime = torrent.Value.updateTime, fileTime = torrent.Value.updateTime.ToFileTimeUtc() };
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true });
        }

        public JsonResult ResetCheckTime()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }
                        torrent.Value.checkTime = DateTime.Today.AddDays(-1);
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true });
        }

        public JsonResult UpdateDetails()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }
                        FileDB.updateFullDetails(torrent.Value);
                        torrent.Value.languages = null;

                        torrent.Value.updateTime = DateTime.UtcNow;
                        FileDB.masterDb[item.Key] = new Models.TorrentInfo() { updateTime = torrent.Value.updateTime, fileTime = torrent.Value.updateTime.ToFileTimeUtc() };
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true });
        }

        public JsonResult UpdateSearchName()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    var toMigrate = new List<(string url, TorrentDetails t, string newKey)>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(torrent.Value.name))
                            torrent.Value.name = torrent.Value.title ?? "";

                        if (string.IsNullOrWhiteSpace(torrent.Value.originalname))
                            torrent.Value.originalname = torrent.Value.title ?? torrent.Value.name ?? "";
                        torrent.Value._sn = StringConvert.SearchName(torrent.Value.name);
                        torrent.Value._so = StringConvert.SearchName(torrent.Value.originalname);
                        // Если ключ бакета изменился (например починили name) — переносим торрент в правильный бакет, чтобы поиск находил по новому ключу
                        string newKey = FileDB.KeyForTorrent(torrent.Value.name, torrent.Value.originalname);
                        if (!string.IsNullOrEmpty(newKey) && newKey != item.Key && newKey.IndexOf(':') > 0)
                            toMigrate.Add((torrent.Key, torrent.Value, newKey));
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);
                    foreach (var (url, t, newKey) in toMigrate)
                    {
                        fdb.Database.Remove(url);
                        FileDB.MigrateTorrentToNewKey(t, newKey);
                    }
                    if (fdb.Database.Count == 0)
                        FileDB.RemoveKeyFromMasterDb(item.Key);
                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true });
        }

        /// <summary>
        /// Scan DB for corrupt entries (null Value, missing name/originalname/trackerName). Read-only, no changes.
        /// </summary>
        public JsonResult FindCorrupt(int sampleSize = 20)
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            int totalTorrents = 0;
            int nullValueCount = 0;
            int missingNameCount = 0;
            int missingOriginalnameCount = 0;
            int missingTrackerNameCount = 0;
            var nullValueSample = new List<object>();
            var missingNameSample = new List<object>();
            var missingOriginalnameSample = new List<object>();
            var missingTrackerNameSample = new List<object>();

            foreach (var item in FileDB.masterDb.ToArray())
            {
                var db = FileDB.OpenRead(item.Key, cache: false);
                if (db == null)
                    continue;

                foreach (var kv in db)
                {
                    totalTorrents++;
                    string fdbKey = item.Key;
                    string url = kv.Key;
                    var t = kv.Value;

                    if (t == null)
                    {
                        nullValueCount++;
                        if (nullValueSample.Count < sampleSize)
                            nullValueSample.Add(new { fdbKey, url });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(t.trackerName))
                    {
                        missingTrackerNameCount++;
                        if (missingTrackerNameSample.Count < sampleSize)
                            missingTrackerNameSample.Add(new { fdbKey, url, title = t.title });
                    }
                    if (string.IsNullOrWhiteSpace(t.name))
                    {
                        missingNameCount++;
                        if (missingNameSample.Count < sampleSize)
                            missingNameSample.Add(new { fdbKey, url, title = t.title });
                    }
                    if (string.IsNullOrWhiteSpace(t.originalname))
                    {
                        missingOriginalnameCount++;
                        if (missingOriginalnameSample.Count < sampleSize)
                            missingOriginalnameSample.Add(new { fdbKey, url, title = t.title });
                    }
                }
            }

            return Json(new
            {
                ok = true,
                totalFdbKeys = FileDB.masterDb.Count,
                totalTorrents,
                corrupt = new
                {
                    nullValue = new { count = nullValueCount, sample = nullValueSample },
                    missingName = new { count = missingNameCount, sample = missingNameSample },
                    missingOriginalname = new { count = missingOriginalnameCount, sample = missingOriginalnameSample },
                    missingTrackerName = new { count = missingTrackerNameCount, sample = missingTrackerNameSample }
                }
            });
        }

        /// <summary>
        /// Remove only corrupt entries where torrent.Value == null (e.g. empty url, broken refs). No other repairs.
        /// </summary>
        public JsonResult RemoveNullValues()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            int totalRemoved = 0;
            int affectedFiles = 0;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                            keysToRemove.Add(torrent.Key);
                    }
                    if (keysToRemove.Count > 0)
                    {
                        foreach (var k in keysToRemove)
                            fdb.Database.Remove(k);
                        totalRemoved += keysToRemove.Count;
                        affectedFiles++;
                        fdb.savechanges = true;
                    }
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true, removed = totalRemoved, affectedFiles });
        }

        /// <summary>
        /// Find duplicate keys X:X (name == originalname after normalization), for example ponies:ponies. Only localhost.
        /// ?tracker=lostfilm — only buckets with torrents of this tracker.
        /// ?excludeNumeric=false — include keys that are purely numeric (1899:1899, 911:911); by default they are excluded, as they are usually valid same-name series.
        /// </summary>
        public JsonResult FindDuplicateKeys(string tracker = null, bool excludeNumeric = true)
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            var duplicateKeys = new List<object>();
            foreach (var item in FileDB.masterDb.ToArray())
            {
                string key = item.Key;
                int colon = key.IndexOf(':');
                if (colon <= 0 || colon >= key.Length - 1)
                    continue;
                string part1 = key.Substring(0, colon);
                string part2 = key.Substring(colon + 1);
                if (!string.Equals(part1, part2, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (excludeNumeric && part1.Length > 0 && part1.All(char.IsDigit))
                    continue;

                int count = 0;
                try
                {
                    var db = FileDB.OpenRead(key, cache: false);
                    count = db.Count;
                    if (!string.IsNullOrWhiteSpace(tracker))
                    {
                        bool hasTracker = db.Values.Any(t => t != null && string.Equals(t.trackerName, tracker.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (!hasTracker)
                            continue;
                    }
                }
                catch
                {
                    continue;
                }

                duplicateKeys.Add(new { key, count });
            }

            return Json(new { ok = true, count = duplicateKeys.Count, keys = duplicateKeys });
        }

        /// <summary>
        /// Remove bucket by key (e.g. old ponies:ponies). Only localhost.
        /// ?key=ponies:ponies — simply remove all records and key.
        /// ?key=ponies:ponies&amp;migrateName=Пони&amp;migrateOriginalname=Ponies — move all torrents to bucket ponies:ponies and remove old key.
        /// </summary>
        public JsonResult RemoveBucket(string key, string migrateName = null, string migrateOriginalname = null)
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            if (string.IsNullOrWhiteSpace(key) || key.IndexOf(':') < 0)
                return Json(new { error = "key required, format: name:originalname (e.g. ponies:ponies)" });

            key = key.Trim();
            if (!FileDB.masterDb.ContainsKey(key))
                return Json(new { error = "key not found", key });

            bool doMigrate = !string.IsNullOrWhiteSpace(migrateName) && !string.IsNullOrWhiteSpace(migrateOriginalname);
            string newKey = doMigrate ? FileDB.KeyForTorrent(migrateName, migrateOriginalname) : null;

            int migrated = 0, removed = 0;
            using (var fdb = FileDB.OpenWrite(key))
            {
                var toMigrate = new List<(string url, TorrentDetails t)>();
                var toRemove = new List<string>();
                foreach (var kv in fdb.Database.ToList())
                {
                    if (kv.Value == null)
                    {
                        toRemove.Add(kv.Key);
                        continue;
                    }
                    if (doMigrate)
                    {
                        kv.Value.name = migrateName;
                        kv.Value.originalname = migrateOriginalname;
                        kv.Value._sn = StringConvert.SearchName(migrateName);
                        kv.Value._so = StringConvert.SearchName(migrateOriginalname);
                        toMigrate.Add((kv.Key, kv.Value));
                    }
                    else
                        toRemove.Add(kv.Key);
                }
                removed = toRemove.Count;
                foreach (var url in toRemove)
                    fdb.Database.Remove(url);
                foreach (var (url, t) in toMigrate)
                {
                    fdb.Database.Remove(url);
                    FileDB.MigrateTorrentToNewKey(t, newKey);
                    migrated++;
                }
                if (fdb.Database.Count == 0)
                    FileDB.RemoveKeyFromMasterDb(key);
                fdb.savechanges = true;
            }

            FileDB.SaveChangesToFile();
            return Json(new
            {
                ok = true,
                key,
                migrated,
                removed,
                newKey = doMigrate ? newKey : (string)null
            });
        }

        /// <summary>
        /// Находит торренты с пустыми _sn или _so полями. Только localhost, read-only.
        /// ?sampleSize=20 — количество примеров для каждого типа проблемы.
        /// </summary>
        public JsonResult FindEmptySearchFields(int sampleSize = 20)
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            int totalTorrents = 0;
            int emptySnCount = 0;
            int emptySoCount = 0;
            int emptyBothCount = 0;
            var emptySnSample = new List<object>();
            var emptySoSample = new List<object>();
            var emptyBothSample = new List<object>();

            foreach (var item in FileDB.masterDb.ToArray())
            {
                var db = FileDB.OpenRead(item.Key, cache: false);
                if (db == null)
                    continue;

                foreach (var kv in db)
                {
                    totalTorrents++;
                    string fdbKey = item.Key;
                    string url = kv.Key;
                    var t = kv.Value;

                    if (t == null)
                        continue;

                    bool hasEmptySn = string.IsNullOrWhiteSpace(t._sn);
                    bool hasEmptySo = string.IsNullOrWhiteSpace(t._so);

                    if (hasEmptySn && hasEmptySo)
                    {
                        emptyBothCount++;
                        if (emptyBothSample.Count < sampleSize)
                            emptyBothSample.Add(new { fdbKey, url, title = t.title, name = t.name, originalname = t.originalname });
                    }
                    else if (hasEmptySn)
                    {
                        emptySnCount++;
                        if (emptySnSample.Count < sampleSize)
                            emptySnSample.Add(new { fdbKey, url, title = t.title, name = t.name, originalname = t.originalname });
                    }
                    else if (hasEmptySo)
                    {
                        emptySoCount++;
                        if (emptySoSample.Count < sampleSize)
                            emptySoSample.Add(new { fdbKey, url, title = t.title, name = t.name, originalname = t.originalname });
                    }
                }
            }

            return Json(new
            {
                ok = true,
                totalFdbKeys = FileDB.masterDb.Count,
                totalTorrents,
                emptySearchFields = new
                {
                    emptySn = new { count = emptySnCount, sample = emptySnSample },
                    emptySo = new { count = emptySoCount, sample = emptySoSample },
                    emptyBoth = new { count = emptyBothCount, sample = emptyBothSample },
                    total = emptySnCount + emptySoCount + emptyBothCount
                }
            });
        }

        /// <summary>
        /// Исправляет пустые _sn и _so поля, вычисляя их из name/originalname/title. Только localhost.
        /// Также обновляет ключи бакетов если они изменились после исправления.
        /// </summary>
        public JsonResult FixEmptySearchFields()
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            int totalFixed = 0;
            int snFixed = 0;
            int soFixed = 0;
            int migrated = 0;
            int affectedBuckets = 0;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    var toMigrate = new List<(string url, TorrentDetails t, string newKey)>();
                    bool bucketChanged = false;

                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }

                        var t = torrent.Value;
                        bool fixedSn = false;
                        bool fixedSo = false;

                        // Исправляем _sn если пустое
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

                        // Исправляем _so если пустое
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

                        // Убеждаемся, что name и originalname заполнены
                        if (string.IsNullOrWhiteSpace(t.name))
                            t.name = t.title ?? "";
                        if (string.IsNullOrWhiteSpace(t.originalname))
                            t.originalname = t.name ?? t.title ?? "";

                        // Пересчитываем _sn и _so если они все еще пустые
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
                            totalFixed++;
                            if (fixedSn) snFixed++;
                            if (fixedSo) soFixed++;

                            // Проверяем, нужно ли мигрировать в другой бакет
                            string newKey = FileDB.KeyForTorrent(t.name, t.originalname);
                            if (!string.IsNullOrEmpty(newKey) && newKey != item.Key && newKey.IndexOf(':') > 0)
                            {
                                toMigrate.Add((torrent.Key, t, newKey));
                                bucketChanged = true;
                            }
                        }
                    }

                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    foreach (var (url, t, newKey) in toMigrate)
                    {
                        fdb.Database.Remove(url);
                        FileDB.MigrateTorrentToNewKey(t, newKey);
                        migrated++;
                    }

                    if (fdb.Database.Count == 0)
                    {
                        FileDB.RemoveKeyFromMasterDb(item.Key);
                        bucketChanged = true;
                    }

                    if (bucketChanged || toMigrate.Count > 0 || keysToRemove.Count > 0)
                    {
                        affectedBuckets++;
                        fdb.savechanges = true;
                    }
                }
            }

            FileDB.SaveChangesToFile();

            // Пересобираем fastdb после исправлений
            try { Controllers.ApiController.getFastdb(update: true); } catch { }

            return Json(new
            {
                ok = true,
                totalFixed,
                snFixed,
                soFixed,
                migrated,
                affectedBuckets
            });
        }
    }
}
