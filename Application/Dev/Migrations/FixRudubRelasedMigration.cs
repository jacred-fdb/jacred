using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Trackers.Rudub;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;

namespace JacRed.Application.Dev.Migrations
{
    /// <summary>
    /// Backfill Rudub <c>relased</c> (and truncated name/originalname) from stored titles.
    /// Does not download torrents — cron parse skips unchanged listings.
    /// </summary>
    public sealed class FixRudubRelasedMigration : DevMigrationBase, IDevMigration
    {
        public const string TrackerName = RudubParser.TrackerName;

        public string Name => "fixRudubRelased";

        public FixRudubRelasedMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run()
        {
            int processed = 0, yearUpdated = 0, namesUpdated = 0, migrated = 0;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var toMigrate = new List<(string url, TorrentDetails t, string newKey)>();
                    bool bucketChanged = false;

                    foreach (var kv in fdb.Database.ToList())
                    {
                        var t = kv.Value;
                        if (t == null || !string.Equals(t.trackerName, TrackerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        processed++;
                        if (!TryPatch(t, out bool yearChanged, out bool namesChanged))
                            continue;

                        bucketChanged = true;
                        if (yearChanged)
                            yearUpdated++;
                        if (namesChanged)
                            namesUpdated++;

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

                    if (bucketChanged)
                        fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            if (namesUpdated > 0 || migrated > 0)
            {
                try { TryRebuildFastDb(); } catch { }
            }

            return new { ok = true, processed, yearUpdated, namesUpdated, migrated };
        }

        /// <summary>
        /// Apply current Rudub title parsing to an existing torrent. Returns true if any field changed.
        /// </summary>
        public static bool TryPatch(TorrentDetails t, out bool yearUpdated, out bool namesUpdated)
        {
            yearUpdated = false;
            namesUpdated = false;
            if (t == null || string.IsNullOrWhiteSpace(t.title))
                return false;

            var (name, originalname, relased) = RudubParser.ParseTitleFields(t.title, t.createTime);

            if (relased > 0 && t.relased != relased)
            {
                t.relased = relased;
                yearUpdated = true;
            }

            if (!string.IsNullOrWhiteSpace(name) && name != t.name)
            {
                t.name = name;
                t._sn = StringConvert.SearchName(name);
                namesUpdated = true;
            }

            if (!string.IsNullOrWhiteSpace(originalname) && originalname != t.originalname)
            {
                t.originalname = originalname;
                t._so = StringConvert.SearchName(originalname);
                namesUpdated = true;
            }

            return yearUpdated || namesUpdated;
        }
    }
}
