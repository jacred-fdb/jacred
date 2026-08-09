using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Korsars
{
    /// <summary>
    /// Parses korsars.pro — phpBB-mod tracker with inline magnets on forum listings.
    /// Forum IDs match Go cron/korsars (hand-picked movie/serial/cartoon sections).
    /// </summary>
    public static class KorsarsParser
    {
        public const string TrackerName = "korsars";
        public const int TopicsPerPage = 50;

        static readonly Regex RowDateRe = new(
            @"<p>([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})</p>",
            RegexOptions.Compiled);

        static readonly Regex RowTopicIdRe = new(@"<a id=""tt-([0-9]+)""", RegexOptions.Compiled);

        static readonly Regex RowTitleRe = new(
            @"<a id=""tt-[0-9]+""[^>]+>\s*<b>([^<]+)</b>\s*</a>",
            RegexOptions.Compiled);

        static readonly Regex RowSidRe = new(
            @"<span class=""seedmed""[^>]*><b>([0-9]+)</b>",
            RegexOptions.Compiled);

        static readonly Regex RowPirRe = new(
            @"<span class=""leechmed""[^>]*><b>([0-9]+)</b>",
            RegexOptions.Compiled);

        static readonly Regex RowSizeRe = new(
            @"href=""\./dl\.php\?id=[0-9]+""[^>]*>([^<]+)</a>",
            RegexOptions.Compiled);

        static readonly Regex RowMagnetRe = new(@"href=""(magnet:[^""]+)""", RegexOptions.Compiled);

        static readonly Regex PagerStartRe = new(
            @"viewforum\.php\?f=[0-9]+(?:&amp;|&)start=([0-9]+)",
            RegexOptions.Compiled);

        static readonly Regex YearRe = new(@"\(([0-9]{4})", RegexOptions.Compiled);

        static readonly Regex TitleSerial3Re = new(
            @"^([^/\[\(]+) / [^/\[\(]+ / ([^/\[\(]+) \[S[0-9]",
            RegexOptions.Compiled);

        static readonly Regex TitleSerial2Re = new(
            @"^([^/\[\(]+) / ([^/\[\(]+) \[S[0-9]",
            RegexOptions.Compiled);

        static readonly Regex TitleSerial1Re = new(
            @"^([^/\[\(]+) \[S[0-9]",
            RegexOptions.Compiled);

        static readonly Regex TitleMovie3Re = new(
            @"^([^/\(]+) / [^/\(]+ / ([^/\(]+) \(",
            RegexOptions.Compiled);

        static readonly Regex TitleMovie2Re = new(
            @"^([^/\(]+) / ([^/\(]+) \(",
            RegexOptions.Compiled);

        static readonly Regex TitleMovie1Re = new(
            @"^([^/\(]+) \(",
            RegexOptions.Compiled);

        static readonly Regex FirstNamePartRe = new(@"(\[|/|\(|\|)", RegexOptions.Compiled);

        static readonly Regex StripTagsRe = new(@"<[^>]+>", RegexOptions.Compiled);
        static readonly Regex WhitespaceRe = new(@"\s+", RegexOptions.Compiled);

        static readonly TimeZoneInfo MoscowTz = ResolveMoscowTz();

        public static string ForumUrl(string host, string cat, int page)
        {
            host = (host ?? "").TrimEnd('/');
            string url = $"{host}/viewforum.php?f={cat}";
            if (page > 0)
                url += $"&start={page * TopicsPerPage}";
            return url;
        }

        public static string TopicUrl(string host, string topicId)
        {
            host = (host ?? "").TrimEnd('/');
            return $"{host}/viewtopic.php?t={topicId}";
        }

        /// <summary>
        /// Zero-based last page from the largest ?start=N pager link (steps of 50).
        /// </summary>
        public static int LastPageFromHtml(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return 0;

            int maxStart = 0;
            foreach (Match m in PagerStartRe.Matches(body))
            {
                if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                    && n > maxStart)
                {
                    maxStart = n;
                }
            }

            return maxStart / TopicsPerPage;
        }

        public static bool LooksLikeLoginForm(string body)
            => !string.IsNullOrEmpty(body)
               && body.Contains("name=\"login_username\"", StringComparison.Ordinal)
               && !body.Contains("id=\"tt-", StringComparison.Ordinal);

        public static string[] CategoryTypes(string cat) => KorsarsCategories.TypesFor(cat);

        /// <summary>
        /// Peel «RUS [/ ALT / ] EN [Sxx] (YEAR) …» into (russian, original, year).
        /// </summary>
        public static (string name, string original, int year) ParseTitle(string title)
        {
            int year = 0;
            var yearMatch = YearRe.Match(title ?? "");
            if (yearMatch.Success)
                int.TryParse(yearMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out year);

            var m = TitleSerial3Re.Match(title ?? "");
            if (m.Success)
                return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim(), year);

            m = TitleSerial2Re.Match(title ?? "");
            if (m.Success)
                return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim(), year);

            m = TitleSerial1Re.Match(title ?? "");
            if (m.Success)
                return (m.Groups[1].Value.Trim(), "", year);

            m = TitleMovie3Re.Match(title ?? "");
            if (m.Success)
                return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim(), year);

            m = TitleMovie2Re.Match(title ?? "");
            if (m.Success)
                return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim(), year);

            m = TitleMovie1Re.Match(title ?? "");
            if (m.Success)
                return (m.Groups[1].Value.Trim(), "", year);

            return ("", "", year);
        }

        public static string FirstTokenTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "";
            return FirstNamePartRe.Split(title, 2)[0].Trim();
        }

        /// <summary>
        /// Parse a forum listing page. <paramref name="canonicalHost"/> is stored in FDB urls
        /// (Go uses Config.Host, not alias).
        /// </summary>
        public static List<TorrentDetails> ParseListingHtml(string body, string cat, string canonicalHost)
        {
            var outList = new List<TorrentDetails>();
            if (string.IsNullOrWhiteSpace(body))
                return outList;

            string[] types = CategoryTypes(cat);
            if (types.Length == 0)
                return outList;

            string host = (canonicalHost ?? "").TrimEnd('/');
            string[] rows = body.Split(new[] { "id=\"tt-" }, StringSplitOptions.None);
            for (int i = 1; i < rows.Length; i++)
            {
                // Re-prefix so RowTopicIdRe (expects the full marker) matches.
                string row = "<a id=\"tt-" + rows[i];

                string id = Match1(RowTopicIdRe, row);
                string title = CleanText(Match1(RowTitleRe, row));
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                    continue;

                DateTime createTime = ParseListingDate(Match1(RowDateRe, row));
                if (createTime == default)
                    continue;

                int.TryParse(Match1(RowSidRe, row), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sid);
                int.TryParse(Match1(RowPirRe, row), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pir);

                string sizeName = CleanText(Match1(RowSizeRe, row)).Replace('\u00A0', ' ').Trim();
                string magnet = WebUtility.HtmlDecode(Match1(RowMagnetRe, row)) ?? "";
                if (string.IsNullOrWhiteSpace(sizeName) || string.IsNullOrWhiteSpace(magnet))
                    continue;

                var (name, original, year) = ParseTitle(title);
                if (string.IsNullOrWhiteSpace(name))
                    name = FirstTokenTitle(title);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                outList.Add(new TorrentDetails
                {
                    trackerName = TrackerName,
                    types = types,
                    url = TopicUrl(host, id),
                    title = title,
                    sid = sid,
                    pir = pir,
                    sizeName = sizeName,
                    magnet = magnet,
                    createTime = createTime,
                    updateTime = DateTime.UtcNow,
                    name = name,
                    originalname = original,
                    relased = year
                });
            }

            return outList;
        }

        /// <summary>Reads «YYYY-MM-DD HH:MM» as Europe/Moscow and returns UTC.</summary>
        public static DateTime ParseListingDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return default;

            if (!DateTime.TryParseExact(
                    s.Trim(),
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var local))
            {
                return default;
            }

            try
            {
                return TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                    MoscowTz);
            }
            catch (ArgumentOutOfRangeException)
            {
                return default;
            }
            catch (ArgumentException)
            {
                return default;
            }
        }

        static string Match1(Regex re, string s)
        {
            var m = re.Match(s ?? "");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        static string CleanText(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            s = StripTagsRe.Replace(s, "");
            s = WebUtility.HtmlDecode(s) ?? "";
            s = s.Replace('\u00A0', ' ');
            s = WhitespaceRe.Replace(s, " ");
            return s.Trim();
        }

        static TimeZoneInfo ResolveMoscowTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }

            try { return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }

            return TimeZoneInfo.CreateCustomTimeZone("MSK", TimeSpan.FromHours(3), "MSK", "MSK");
        }
    }
}
