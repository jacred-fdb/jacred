using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
namespace JacRed.Application.Dev.Migrations
{
    public sealed class FixAnilibertyUrlsMigration : IDevMigration
    {
        public string Name => "migrateAnilibertyUrls";

        public object Run()
        {


            int totalProcessed = 0;
            int totalUpdated = 0;
            int totalSkipped = 0;
            int totalErrors = 0;
            var errors = new List<string>();

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var toUpdate = new List<(string oldUrl, TorrentDetails torrent, string newUrl)>();

                    foreach (var kv in fdb.Database)
                    {
                        var torrent = kv.Value;
                        if (torrent == null)
                            continue;

                        // Only process aniliberty torrents
                        if (!string.Equals(torrent.trackerName, "aniliberty", StringComparison.OrdinalIgnoreCase))
                            continue;

                        totalProcessed++;

                        // Skip if URL already has hash parameter
                        if (kv.Key.Contains("?hash="))
                        {
                            totalSkipped++;
                            continue;
                        }

                        // Extract hash from magnet link
                        // Format: magnet:?xt=urn:btih:{hash}...
                        string hash = null;
                        if (!string.IsNullOrWhiteSpace(torrent.magnet))
                        {
                            var match = Regex.Match(torrent.magnet, @"urn:btih:([a-fA-F0-9]{40})", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                hash = match.Groups[1].Value;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(hash))
                        {
                            totalErrors++;
                            errors.Add($"No hash found in magnet for URL: {kv.Key}");
                            continue;
                        }

                        // Build new URL with hash parameter
                        string oldUrl = kv.Key;
                        string newUrl = oldUrl.Contains("?")
                            ? $"{oldUrl}&hash={hash}"
                            : $"{oldUrl}?hash={hash}";

                        // Skip if new URL is same as old (shouldn't happen, but safety check)
                        if (oldUrl == newUrl)
                        {
                            totalSkipped++;
                            continue;
                        }

                        toUpdate.Add((oldUrl, torrent, newUrl));
                    }

                    // Update URLs: remove old entries and add with new URLs
                    foreach (var (oldUrl, torrent, newUrl) in toUpdate)
                    {
                        try
                        {
                            // Remove old entry
                            fdb.Database.Remove(oldUrl);

                            // Update torrent URL
                            torrent.url = newUrl;

                            // Add with new URL
                            fdb.Database[newUrl] = torrent;

                            totalUpdated++;
                        }
                        catch (Exception ex)
                        {
                            totalErrors++;
                            errors.Add($"Error updating {oldUrl}: {ex.Message}");
                        }
                    }

                    if (toUpdate.Count > 0)
                        fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();

            return new
            {
                ok = true,
                totalProcessed,
                totalUpdated,
                totalSkipped,
                totalErrors,
                errors = errors.Take(10).ToList() // Return first 10 errors if any
            };

        }
    }
}
