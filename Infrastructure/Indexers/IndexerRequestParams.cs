using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Indexers
{
    public static class IndexerRequestParams
    {
        static readonly Regex ImdbKpRegex = new Regex(@"^(tt|kp)[0-9]+$", RegexOptions.IgnoreCase);

        public static string StripWrappingQuotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var s = value.Trim();
            if ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("'") && s.EndsWith("'")))
                s = s.Substring(1, s.Length - 2).Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        public static string NormalizeImdbId(string value)
        {
            var s = StripWrappingQuotes(value);
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (s.StartsWith("kp", StringComparison.OrdinalIgnoreCase)) return s.ToLowerInvariant();
            if (s.StartsWith("tt", StringComparison.OrdinalIgnoreCase)) return s.ToLowerInvariant();
            if (Regex.IsMatch(s, @"^\d{7,10}$")) return "tt" + s;
            return s;
        }

        public static bool IsImdbOrKpQuery(string query)
        {
            return !string.IsNullOrWhiteSpace(query) && ImdbKpRegex.IsMatch(query.Trim());
        }

        public static string NormalizeQuery(string query)
        {
            var q = StripWrappingQuotes(query);
            if (string.IsNullOrWhiteSpace(q)) return null;
            if (Regex.IsMatch(q.Trim(), @"^\d{7,10}$")) return "tt" + q.Trim();
            return q.Trim();
        }

        public static string ImDbIdFromQuery(IQueryCollection query)
        {
            foreach (var key in new[] { "imdbid", "imdb_id", "imdbId" })
            {
                if (query.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                    return NormalizeImdbId(v.ToString());
            }
            return null;
        }

        public static string ResolveSearchQuery(IQueryCollection query)
        {
            if (query.TryGetValue("q", out var q) && !string.IsNullOrWhiteSpace(q))
                return NormalizeQuery(q.ToString());
            if (query.TryGetValue("Query", out var Q) && !string.IsNullOrWhiteSpace(Q))
                return NormalizeQuery(Q.ToString());
            if (query.TryGetValue("query", out var q2) && !string.IsNullOrWhiteSpace(q2))
                return NormalizeQuery(q2.ToString());
            return ImDbIdFromQuery(query);
        }

        public static bool TvdbIdOnly(IQueryCollection query, string resolvedQuery)
        {
            if (!string.IsNullOrWhiteSpace(resolvedQuery)) return false;
            foreach (var key in new[] { "tvdbid", "rid" })
            {
                if (query.ContainsKey(key) && !string.IsNullOrWhiteSpace(query[key]))
                    return true;
            }
            return false;
        }

        public static List<int> CategoriesFromQuery(IQueryCollection query)
        {
            var cats = new List<int>();
            foreach (var key in query.Keys.Where(k =>
                k.StartsWith("Category[", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Category[]", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("cat", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Category", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var val in query[key])
                {
                    foreach (var part in (val ?? "").Split(','))
                    {
                        if (int.TryParse(part.Trim(), out int n) && n > 0)
                            cats.Add(n);
                    }
                }
            }
            return cats.Distinct().ToList();
        }

        /// <summary>Jackett-style tracker filter: Tracker[], Tracker, tracker.</summary>
        public static List<string> TrackersFromQuery(IQueryCollection query)
        {
            var trackers = new List<string>();
            foreach (var key in query.Keys.Where(k =>
                k.StartsWith("Tracker[", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Tracker[]", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Tracker", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("tracker", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var val in query[key])
                {
                    foreach (var part in (val ?? "").Split(','))
                    {
                        var trimmed = part.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            trackers.Add(trimmed);
                    }
                }
            }

            return trackers
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static int? SeasonFromQuery(IQueryCollection query)
        {
            if (!query.TryGetValue("season", out var v) || !int.TryParse(v, out int n) || n <= 0) return null;
            return n;
        }

        public static int? EpisodeFromQuery(IQueryCollection query)
        {
            if (query.TryGetValue("ep", out var ep) && int.TryParse(ep, out int n) && n > 0) return n;
            if (query.TryGetValue("episode", out var episode) && int.TryParse(episode, out n) && n > 0) return n;
            return null;
        }

        public static (int? limit, int offset) LimitOffsetFromQuery(IQueryCollection query)
        {
            int offset = 0;
            if (query.TryGetValue("offset", out var off) && int.TryParse(off, out int o) && o > 0)
                offset = o;

            if (!query.TryGetValue("limit", out var lim) || !int.TryParse(lim, out int l) || l <= 0)
                return (null, offset);

            return (l, offset);
        }

        public static int YearFromQuery(IQueryCollection query)
        {
            if (query.TryGetValue("year", out var y) && int.TryParse(y, out int year) && year > 0)
                return year;
            return 0;
        }

        public static int IsSerialFromTorznabAction(string t)
        {
            if (t == "moviesearch" || t == "movie") return 0;
            if (t == "tvsearch" || t == "tv") return 1;
            return -1;
        }

        public static bool IsCardMetadataSearch(string title, string titleOriginal, int? isSerial, List<int> categories, string genres)
        {
            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(titleOriginal)) return true;
            if (isSerial.HasValue && isSerial.Value >= 0) return true;
            if (categories != null && categories.Count > 0) return true;
            if (!string.IsNullOrWhiteSpace(genres)) return true;
            return false;
        }

        public static (string ru, string en) SplitBilingualQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || !query.Contains(" / ")) return (null, null);
            var parts = query.Split(new[] { " / " }, 2, StringSplitOptions.None);
            var left = parts[0].Trim();
            var right = parts.Length > 1 ? parts[1].Trim() : "";
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return (null, null);
            bool leftCyr = Regex.IsMatch(left, @"[а-яА-ЯёЁ]");
            bool rightLat = Regex.IsMatch(right, @"[a-zA-Z]");
            if (leftCyr && rightLat) return (left, right);
            if (Regex.IsMatch(left, @"[a-zA-Z]") && Regex.IsMatch(right, @"[а-яА-ЯёЁ]")) return (right, left);
            return (left, right);
        }

        public static string StripTrailingYear(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            var m = Regex.Match(query.Trim(), @"^(.+?)\s+(19|20)\d{2}$");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
    }
}
