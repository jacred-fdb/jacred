using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;
using JacRed.Infrastructure.Trackers.Knaben;
namespace JacRed.Application.Dev.Migrations
{
  public sealed class FixKnabenNamesMigration : DevMigrationBase, IDevMigration
  {
    public string Name => "fixKnabenNames";

    public FixKnabenNamesMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

    public object Run()
    {


const string trackerName = "knaben";
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
            string source = !string.IsNullOrWhiteSpace(t.title) ? t.title : (t.name ?? "");
            if (string.IsNullOrWhiteSpace(source)) continue;

            var (newName, newRelased) = KnabenParser.ParseNameAndYear(source);
            if (string.IsNullOrWhiteSpace(newName)) continue;

            string trackerSuffix = "";
            var suffixMatch = Regex.Match(source, @"\s+\|\s+[^|]+$");
            if (suffixMatch.Success) trackerSuffix = suffixMatch.Value;

            string newTitle = KnabenParser.BuildTitleForFileDB(source.TrimEnd()) + trackerSuffix;

            bool nameChanged = newName != t.name || newName != t.originalname;
            bool relasedChanged = newRelased != t.relased;
            bool titleChanged = newTitle != t.title;

            if (!nameChanged && !relasedChanged && !titleChanged) continue;

            t.name = newName;
            t.originalname = newName;
            t.relased = newRelased;
            t.title = newTitle;
            t._sn = StringConvert.SearchName(newName);
            t._so = StringConvert.SearchName(newName);
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
