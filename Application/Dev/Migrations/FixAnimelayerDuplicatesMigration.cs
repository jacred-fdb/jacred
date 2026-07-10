using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
namespace JacRed.Application.Dev.Migrations
{
  public sealed class FixAnimelayerDuplicatesMigration : IDevMigration
  {
    public string Name => "fixAnimelayerDuplicates";

    public object Run()
    {


int totalProcessed = 0;
int totalFixed = 0;
int totalRemoved = 0;
var errors = new List<string>();

// Dictionary to track hex ID -> list of (bucket key, url, torrent)
var idMap = new Dictionary<string, List<(string bucketKey, string url, TorrentDetails torrent)>>(StringComparer.OrdinalIgnoreCase);

// First pass: collect all animelayer torrents grouped by hex ID
foreach (var item in FileDB.masterDb.ToArray())
{
    var db = FileDB.OpenRead(item.Key, cache: false);
    foreach (var kv in db)
    {
        var torrent = kv.Value;
        if (torrent == null)
            continue;

        if (!string.Equals(torrent.trackerName, "animelayer", StringComparison.OrdinalIgnoreCase))
            continue;

        totalProcessed++;

        // Extract hex ID from URL: /torrent/68e28fee5b4534637209fdf2/
        var match = Regex.Match(kv.Key, @"/torrent/([a-f0-9]+)/?", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            errors.Add($"Could not extract ID from URL: {kv.Key}");
            continue;
        }

        string hexId = match.Groups[1].Value.ToLowerInvariant();
        if (!idMap.ContainsKey(hexId))
            idMap[hexId] = new List<(string, string, TorrentDetails)>();

        idMap[hexId].Add((item.Key, kv.Key, torrent));
    }
}

// Second pass: fix duplicates
foreach (var item in FileDB.masterDb.ToArray())
{
    using (var fdb = FileDB.OpenWrite(item.Key))
    {
        var toRemove = new List<string>();
        var toUpdate = new List<(string oldUrl, TorrentDetails torrent, string newUrl)>();

        foreach (var kv in fdb.Database)
        {
            var torrent = kv.Value;
            if (torrent == null)
                continue;

            if (!string.Equals(torrent.trackerName, "animelayer", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip if already HTTPS
            if (kv.Key.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;

            // Convert HTTP to HTTPS
            if (kv.Key.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                string newUrl = kv.Key.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

                // Check if HTTPS version already exists
                if (fdb.Database.ContainsKey(newUrl))
                {
                    // HTTPS version exists, remove HTTP duplicate
                    toRemove.Add(kv.Key);
                    totalRemoved++;
                }
                else
                {
                    // Migrate HTTP to HTTPS
                    toUpdate.Add((kv.Key, torrent, newUrl));
                    totalFixed++;
                }
            }
        }

        // Remove HTTP duplicates
        foreach (var oldUrl in toRemove)
        {
            fdb.Database.Remove(oldUrl);
        }

        // Migrate HTTP to HTTPS
        foreach (var (oldUrl, torrent, newUrl) in toUpdate)
        {
            fdb.Database.Remove(oldUrl);
            torrent.url = newUrl;
            fdb.Database[newUrl] = torrent;
        }

        if (toRemove.Count > 0 || toUpdate.Count > 0)
            fdb.savechanges = true;
    }
}

FileDB.SaveChangesToFile();

return new
{
    ok = true,
    totalProcessed,
    totalFixed,
    totalRemoved,
    totalErrors = errors.Count,
    errors = errors.Take(10).ToList()
};

    }
  }
}
