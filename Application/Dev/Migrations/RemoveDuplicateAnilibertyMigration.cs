using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
namespace JacRed.Application.Dev.Migrations
{
  public sealed class RemoveDuplicateAnilibertyMigration : IDevMigration
  {
    public string Name => "removeDuplicateAniliberty";

    public object Run()
    {


int totalProcessed = 0;
int totalRemoved = 0;
var duplicatesInfo = new List<object>();

// Dictionary to track magnet hash -> (bucket key, url, torrent, updateTime)
var hashMap = new Dictionary<string, List<(string bucketKey, string url, TorrentDetails torrent, DateTime updateTime)>>();

// First pass: collect all aniliberty torrents grouped by magnet hash
foreach (var item in FileDB.masterDb.ToArray())
{
    var db = FileDB.OpenRead(item.Key, cache: false);
    foreach (var kv in db)
    {
        var torrent = kv.Value;
        if (torrent == null)
            continue;

        // Only process aniliberty torrents
        if (!string.Equals(torrent.trackerName, "aniliberty", StringComparison.OrdinalIgnoreCase))
            continue;

        totalProcessed++;

        // Extract hash from magnet link
        string hash = null;
        if (!string.IsNullOrWhiteSpace(torrent.magnet))
        {
            var match = Regex.Match(torrent.magnet, @"urn:btih:([a-fA-F0-9]{40})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                hash = match.Groups[1].Value.ToLowerInvariant();
            }
        }

        if (string.IsNullOrWhiteSpace(hash))
            continue;

        if (!hashMap.ContainsKey(hash))
            hashMap[hash] = new List<(string, string, TorrentDetails, DateTime)>();

        hashMap[hash].Add((item.Key, kv.Key, torrent, torrent.updateTime));
    }
}

// Second pass: remove duplicates, keeping the one with latest updateTime
foreach (var hashGroup in hashMap)
{
    if (hashGroup.Value.Count <= 1)
        continue; // No duplicates

    // Sort by updateTime descending, then by url (for consistency)
    var sorted = hashGroup.Value.OrderByDescending(x => x.updateTime)
                               .ThenBy(x => x.url)
                               .ToList();

    // Keep the first one (most recent), mark others for removal
    var toKeep = sorted[0];
    var toRemove = sorted.Skip(1).ToList();

    duplicatesInfo.Add(new
    {
        hash = hashGroup.Key,
        title = toKeep.torrent.title,
        keepUrl = toKeep.url,
        keepBucket = toKeep.bucketKey,
        keepUpdateTime = toKeep.updateTime,
        removeCount = toRemove.Count,
        removeUrls = toRemove.Select(x => new { url = x.url, bucket = x.bucketKey, updateTime = x.updateTime }).ToList()
    });

    // Remove duplicates from their respective buckets
    foreach (var (bucketKey, url, torrent, updateTime) in toRemove)
    {
        try
        {
            using (var fdb = FileDB.OpenWrite(bucketKey))
            {
                if (fdb.Database.ContainsKey(url))
                {
                    fdb.Database.Remove(url);
                    fdb.savechanges = true;
                    totalRemoved++;
                }
            }
        }
        catch (Exception ex)
        {
            duplicatesInfo.Add(new { error = $"Failed to remove {url} from {bucketKey}: {ex.Message}" });
        }
    }
}

FileDB.SaveChangesToFile();

return new
{
    ok = true,
    totalProcessed,
    totalRemoved,
    duplicatesFound = duplicatesInfo.Count,
    duplicates = duplicatesInfo.Take(50).ToList() // Return first 50 duplicates info
};

    }
  }
}
