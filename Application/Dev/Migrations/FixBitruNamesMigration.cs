using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;
using JacRed.Infrastructure.Trackers.Bitru;
namespace JacRed.Application.Dev.Migrations
{
    public sealed class FixBitruNamesMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "fixBitruNames";

        public FixBitruNamesMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run()
        {


            const string trackerName = "bitru";
            int processed = 0, updated = 0, migrated = 0;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var toMigrate = new List<(string url, TorrentDetails t, string newKey)>();

                    foreach (var kv in fdb.Database.ToList())
                    {
                        var t = kv.Value;
                        if (t == null || !string.Equals(t.trackerName, trackerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        processed++;
                        string newName = BitruApiParser.CleanTitleForSearch(t.name ?? "")?.Trim();
                        string newOriginalname = BitruApiParser.CleanTitleForSearch(t.originalname ?? "")?.Trim();
                        if (string.IsNullOrWhiteSpace(newName)) newName = (t.name ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(newOriginalname)) newOriginalname = (t.originalname ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(newOriginalname)) newOriginalname = newName;

                        if (newName == t.name && newOriginalname == t.originalname)
                            continue;

                        t.name = newName;
                        t.originalname = newOriginalname;
                        t._sn = StringConvert.SearchName(newName);
                        t._so = StringConvert.SearchName(newOriginalname);
                        updated++;

                        string newKey = FileDB.KeyForTorrent(t.name, t.originalname);
                        if (!string.IsNullOrEmpty(newKey) && newKey != item.Key && newKey.IndexOf(':') > 0)
                            toMigrate.Add((kv.Key, t, newKey));
                    }

                    foreach (var (url, t, newKey) in toMigrate)
                    {
                        fdb.Database.Remove(url);
                        FileDB.MigrateTorrentToNewKey(t, newKey);
                        migrated++;
                    }

                    if (fdb.Database.Count == 0)
                        FileDB.RemoveKeyFromMasterDb(item.Key);

                    if (updated > 0 || migrated > 0)
                        fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            try { TryRebuildFastDb(); } catch { }

            return new { ok = true, processed, updated, migrated };

        }
    }
}
