using System;
using System.Collections.Generic;
using System.Linq;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>
    /// Shared tracker name parsing/matching for v1 torrents, Jackett, and Torznab.
    /// Stored <c>trackerName</c> may be comma-joined after duplicate merge (e.g. "kinozal, rutracker").
    /// </summary>
    public static class TrackerNameMatching
    {
        public static List<string> ParseList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<string>();

            return value
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(i => i.Trim())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static HashSet<string> ToAllowSet(IEnumerable<string> trackers)
        {
            return new HashSet<string>(
                (trackers ?? Enumerable.Empty<string>())
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Select(i => i.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when <paramref name="trackerName"/> is empty-filter (allow all), or any comma-separated
        /// part matches the allowlist (OrdinalIgnoreCase).
        /// </summary>
        public static bool Matches(string trackerName, IReadOnlyCollection<string> trackers)
        {
            if (trackers == null || trackers.Count == 0)
                return true;

            return Matches(trackerName, ToAllowSet(trackers));
        }

        public static bool Matches(string trackerName, HashSet<string> allowed)
        {
            if (allowed == null || allowed.Count == 0)
                return true;
            if (string.IsNullOrWhiteSpace(trackerName))
                return false;

            foreach (var part in trackerName.Split(','))
            {
                var name = part.Trim();
                if (name.Length > 0 && allowed.Contains(name))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// When route <c>{indexer}</c> / <c>{status}</c> is a specific tracker (not empty/"all"/
        /// Jackett's virtual "status:healthy" selector/numeric Prowlarr id), ensure it is included
        /// in the request tracker filter.
        /// </summary>
        public static void ApplyIndexerPathFilter(IndexerSearchRequest req, string indexer)
        {
            if (req == null || IsAllIndexer(indexer))
                return;

            // Prowlarr paths use numeric ids (e.g. /api/v1/indexer/1/newznab) — not tracker slugs.
            if (int.TryParse(indexer.Trim(), out _))
                return;

            var name = indexer.Trim();
            req.Trackers ??= new List<string>();
            if (!req.Trackers.Any(t => t.Equals(name, StringComparison.OrdinalIgnoreCase)))
                req.Trackers.Add(name);
            if (string.IsNullOrWhiteSpace(req.Tracker))
                req.Tracker = name;
        }

        public static bool IsAllIndexer(string indexer) =>
            string.IsNullOrWhiteSpace(indexer) ||
            indexer.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            indexer.Equals("status:healthy", StringComparison.OrdinalIgnoreCase);
    }
}
