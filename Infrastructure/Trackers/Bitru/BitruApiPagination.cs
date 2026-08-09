using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Models.tParse;

namespace JacRed.Infrastructure.Trackers.Bitru
{
    /// <summary>
    /// BitRu api.php date cursors.
    /// Official docs label after_date/before_date opposite to live behavior:
    /// request after_date=X → items with added strictly less than X (older);
    /// request before_date=X → items with added strictly greater than X (newer).
    /// Response after_date = max(added), before_date = min(added) on the page.
    /// Older pagination: next request after_date = previous result.before_date.
    /// </summary>
    public static class BitruApiPagination
    {
        public const int MaxPagesHardLimit = 50;
        public const string AfterDateParam = "after_date";

        static readonly Regex DetailsIdRegex = new(@"[?&]id=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static int ClampPages(int pages) => Math.Max(1, Math.Min(MaxPagesHardLimit, pages));

        public static int ClampLimit(int limit) => Math.Max(1, Math.Min(100, limit));

        public static bool TryParseUnix(object value, out long unix)
        {
            unix = 0;
            if (value == null)
                return false;
            if (value is long l)
            {
                unix = l;
                return l != 0;
            }
            if (value is int i)
            {
                unix = i;
                return i != 0;
            }
            if (value is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                unix = parsed;
                return parsed != 0;
            }
            return false;
        }

        public static Dictionary<string, object> BuildRequestParams(int limit, long? olderThanUnix)
        {
            var parms = new Dictionary<string, object>
            {
                { "limit", ClampLimit(limit) },
                { "category", BitruCategories.RequestCategories }
            };
            if (olderThanUnix.HasValue)
                parms[AfterDateParam] = olderThanUnix.Value.ToString(CultureInfo.InvariantCulture);
            return parms;
        }

        /// <summary>
        /// Cursor for the next older page from this response.
        /// Returns false when missing, zero, or unchanged vs previousCursor.
        /// </summary>
        public static bool TryGetNextOlderPageCursor(BitruApiResult result, long? previousCursor, out long nextCursor)
        {
            nextCursor = 0;
            if (result == null || !TryParseUnix(result.BeforeDate, out nextCursor))
                return false;
            if (previousCursor.HasValue && previousCursor.Value == nextCursor)
                return false;
            return true;
        }

        public static bool IsDuplicatePage(IReadOnlyCollection<long> previousIds, IReadOnlyCollection<long> currentIds)
        {
            if (previousIds == null || currentIds == null || currentIds.Count == 0)
                return false;
            if (previousIds.Count == 0)
                return false;
            var prev = previousIds as HashSet<long> ?? previousIds.ToHashSet();
            return currentIds.All(prev.Contains);
        }

        public static bool TryExtractTorrentId(string url, out long id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(url))
                return false;
            var m = DetailsIdRegex.Match(url);
            if (!m.Success || !long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                return false;
            return id != 0;
        }

        public static HashSet<long> CollectTorrentIds(IEnumerable<string> urls)
        {
            var ids = new HashSet<long>();
            if (urls == null)
                return ids;
            foreach (string url in urls)
            {
                if (TryExtractTorrentId(url, out long id))
                    ids.Add(id);
            }
            return ids;
        }
    }
}
