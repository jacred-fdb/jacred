using System.Collections.Generic;
using System.Linq;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;

namespace JacRed.Application.Dev.Migrations
{
    public sealed class CleanupMigrations : DevMigrationBase, IDevMigration
    {
        public string Name => "cleanup";

        public CleanupMigrations(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object RemoveNullValues()
        {


            int totalRemoved = 0;
            int affectedFiles = 0;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database.Where(torrent => torrent.Value == null))
                        keysToRemove.Add(torrent.Key);
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
            return new { ok = true, removed = totalRemoved, affectedFiles };

        }

        public object RemoveBucket(string key, string migrateName = null, string migrateOriginalname = null)
        {


            if (string.IsNullOrWhiteSpace(key) || key.IndexOf(':') < 0)
                return new { error = "key required, format: name:originalname (e.g. ponies:ponies)" };

            key = key.Trim();
            if (!FileDB.masterDb.ContainsKey(key))
                return new { error = "key not found", key };

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
            return new
            {
                ok = true,
                key,
                migrated,
                removed,
                newKey = doMigrate ? newKey : (string)null
            };

        }

        public object FixEmptySearchFields()
        {


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
            try { TryRebuildFastDb(); } catch { }

            return new
            {
                ok = true,
                totalFixed,
                snFixed,
                soFixed,
                migrated,
                affectedBuckets
            };

        }
    }
}
