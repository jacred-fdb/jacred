using JacRed.Models.Api;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>Maps JacRed <see cref="Result"/> rows to Prowlarr Search Feed (ReleaseResource) JSON.</summary>
    public static class ProwlarrSearchFormatter
    {
        const int JacRedIndexerId = 1;
        const string JacRedIndexerName = "JacRed (all trackers)";

        static readonly Dictionary<int, string> CategoryNames = new()
        {
            [2000] = "Movies",
            [2010] = "Movies/Foreign",
            [5000] = "TV",
            [5020] = "TV/Foreign",
            [5070] = "TV/Anime",
            [5080] = "TV/Documentary",
        };

        public static List<object> MapReleases(IEnumerable<Result> results, bool enrichTitles)
        {
            var list = new List<object>();
            foreach (var r in results)
                list.Add(MapRelease(r, enrichTitles));
            return list;
        }

        public static object MapRelease(Result torrent, bool enrichTitles)
        {
            string title = torrent.Title ?? "Unknown";
            var voices = torrent.info?.voices?.ToList() ?? new List<string>();
            string displayTitle = enrichTitles && voices.Count > 0
                ? $"{title} | [{string.Join(' ', voices)}].rus"
                : title;

            string magnet = torrent.MagnetUri ?? "";
            string details = torrent.Details;
            string infoHash = TorznabXmlFormatter.TryExtractInfoHash(magnet);
            string guid = infoHash ?? TorznabXmlFormatter.StableGuid(displayTitle);
            long sizeBytes = TorznabXmlFormatter.GetSizeBytes(torrent);

            DateTime publishDate = torrent.PublishDate == default || torrent.PublishDate.Year < 2000
                ? DateTime.UtcNow
                : DateTime.SpecifyKind(
                    torrent.PublishDate.Kind == DateTimeKind.Unspecified
                        ? torrent.PublishDate
                        : torrent.PublishDate.ToUniversalTime(),
                    DateTimeKind.Utc);

            var ageSpan = DateTime.UtcNow - publishDate.ToUniversalTime();
            var categories = BuildCategories(torrent);

            string magnetUrl = magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ? magnet : null;
            string downloadUrl = !string.IsNullOrWhiteSpace(magnet) ? magnet : details;
            string infoUrl = !string.IsNullOrWhiteSpace(details) && details.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? details
                : null;

            return new
            {
                guid,
                age = Math.Max(0, (int)ageSpan.TotalDays),
                ageHours = Math.Max(0, ageSpan.TotalHours),
                ageMinutes = Math.Max(0, ageSpan.TotalMinutes),
                size = sizeBytes,
                indexerId = JacRedIndexerId,
                indexer = string.IsNullOrWhiteSpace(torrent.Tracker) ? JacRedIndexerName : torrent.Tracker,
                title = displayTitle,
                sortTitle = displayTitle,
                publishDate,
                downloadUrl,
                magnetUrl,
                infoUrl,
                commentUrl = infoUrl,
                categories,
                protocol = "torrent",
                infoHash,
                seeders = torrent.Seeders,
                leechers = torrent.Peers
            };
        }

        static List<object> BuildCategories(Result torrent)
        {
            var cats = new List<object>();
            if (torrent.Category != null)
            {
                foreach (int id in torrent.Category)
                {
                    string name = !string.IsNullOrWhiteSpace(torrent.CategoryDesc) && torrent.Category.Count == 1
                        ? torrent.CategoryDesc
                        : (CategoryNames.TryGetValue(id, out var n) ? n : id.ToString());
                    cats.Add(new { id, name, subCategories = Array.Empty<object>() });
                }
            }

            if (cats.Count == 0)
            {
                int id = 2000;
                string name = CategoryNames[id];
                if (!string.IsNullOrWhiteSpace(torrent.CategoryDesc))
                    name = torrent.CategoryDesc;
                cats.Add(new { id, name, subCategories = Array.Empty<object>() });
            }

            return cats;
        }
    }
}
