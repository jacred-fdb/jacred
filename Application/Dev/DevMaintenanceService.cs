using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;

namespace JacRed.Application.Dev
{
    public class DevMaintenanceService : IDevMaintenanceService
    {

        public object UpdateSize()
        {

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
            return new { ok = true };
        }

        public object ResetCheckTime()
        {

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
            return new { ok = true };
        }

        public object UpdateDetails()
        {

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
            return new { ok = true };
        }

        public object UpdateSearchName()
        {

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
            return new { ok = true };
        }

    }
}
