using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Anibelka
{
    /// <summary>
    /// Parses anibelka.com — anime-only phpBB tracker.
    /// Stays anonymous on purpose: a logged-in .torrent embeds a personal passkey.
    /// </summary>
    public static class AnibelkaParser
    {
        public const string TrackerName = "anibelka";
        public const int TopicsPerPage = 15;

        static readonly Regex RowTopicRe = new(
            @"href=""\./viewtopic\.php\?t=(\d+)[^""]*""\s+class=""topictitle"">(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex PageStartRe = new(@"start=(\d+)", RegexOptions.Compiled);

        static readonly Regex TorrentLinkRe = new(
            @"href=""\./download/file\.php\?id=(\d+)[^""]*""[^>]*tooltip=""Скачать торрент""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex SizeRe = new(
            @"Размер:\s*<b>([0-9.,]+)&nbsp;(КБ|МБ|ГБ|ТБ)</b>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex AddedRe = new(
            @"Добавлен:\s*<b>\s*<span[^>]*>([^<]+)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex SeedRe = new(
            @"Сидеров:\s*<span class=""seed"">\s*<b>(\d+)</b>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex LeechRe = new(
            @"Личеров:\s*<span class=""leech"">\s*<b>(\d+)</b>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex TagRe = new(@"^\[(\w+)\]\s*", RegexOptions.Compiled);
        static readonly Regex YearRe = new(@"\[(\d{4})", RegexOptions.Compiled);
        static readonly Regex RuDateRe = new(
            @"^(\d{1,2})\s+([А-Яа-яЁё]+)\s+(\d{4})(?:,\s*(\d{2}):(\d{2}))?",
            RegexOptions.Compiled);
        static readonly Regex StripTagsRe = new(@"<[^>]+>", RegexOptions.Compiled);
        static readonly Regex WhitespaceRe = new(@"\s+", RegexOptions.Compiled);

        static readonly Dictionary<string, int> RuMonths = new(StringComparer.OrdinalIgnoreCase)
        {
            ["янв"] = 1, ["фев"] = 2, ["мар"] = 3,
            ["апр"] = 4, ["май"] = 5, ["июн"] = 6,
            ["июл"] = 7, ["авг"] = 8, ["сен"] = 9,
            ["окт"] = 10, ["ноя"] = 11, ["дек"] = 12,
        };

        static readonly TimeZoneInfo MoscowTz = ResolveMoscowTz();

        public static string ForumUrl(string host, string sectionId, int page)
        {
            host = (host ?? "").TrimEnd('/');
            if (page <= 0)
                return $"{host}/viewforum.php?f={sectionId}";
            return $"{host}/viewforum.php?f={sectionId}&start={page * TopicsPerPage}";
        }

        public static string TopicUrl(string host, string topicId)
        {
            host = (host ?? "").TrimEnd('/');
            return $"{host}/viewtopic.php?t={topicId}";
        }

        public static string TorrentDownloadUrl(string host, string torrentId)
        {
            host = (host ?? "").TrimEnd('/');
            return $"{host}/download/file.php?id={torrentId}";
        }

        /// <summary>
        /// Zero-based last page from the largest ?start=N link.
        /// </summary>
        public static int LastPageFromHtml(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return 0;

            int maxStart = 0;
            foreach (Match m in PageStartRe.Matches(body))
            {
                if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                    && n > maxStart)
                {
                    maxStart = n;
                }
            }

            return maxStart / TopicsPerPage;
        }

        public static List<AnibelkaListingItem> ParseListingHtml(string body)
        {
            var outList = new List<AnibelkaListingItem>();
            if (string.IsNullOrWhiteSpace(body))
                return outList;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in RowTopicRe.Matches(body))
            {
                string title = CleanText(m.Groups[2].Value);
                // Pinned service topics hold no torrent and have no [tag] prefix.
                if (string.IsNullOrWhiteSpace(title) || !title.StartsWith('[')
                    || !seen.Add(m.Groups[1].Value))
                {
                    continue;
                }

                outList.Add(new AnibelkaListingItem
                {
                    TopicId = m.Groups[1].Value,
                    Title = title
                });
            }

            return outList;
        }

        public static bool TryParseTopicHtml(string body, out AnibelkaTopicInfo info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(body))
                return false;

            var torrentMatch = TorrentLinkRe.Match(body);
            if (!torrentMatch.Success)
                return false;

            info = new AnibelkaTopicInfo { TorrentId = torrentMatch.Groups[1].Value };

            var sizeMatch = SizeRe.Match(body);
            if (sizeMatch.Success)
                info.SizeName = $"{sizeMatch.Groups[1].Value.Trim()} {sizeMatch.Groups[2].Value}";

            var seedMatch = SeedRe.Match(body);
            if (seedMatch.Success
                && int.TryParse(seedMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sid))
                info.Sid = sid;

            var leechMatch = LeechRe.Match(body);
            if (leechMatch.Success
                && int.TryParse(leechMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pir))
                info.Pir = pir;

            var addedMatch = AddedRe.Match(body);
            if (addedMatch.Success)
                info.CreateTime = ParseRuDate(WebUtility.HtmlDecode(addedMatch.Groups[1].Value));

            if (info.CreateTime == default)
                info.CreateTime = DateTime.UtcNow;

            return true;
        }

        /// <summary>
        /// Splits a listing title into (name, original, year).
        /// Original is the first Latin-only slash part (not simply the second).
        /// </summary>
        public static (string name, string original, int year) ParseTitle(string title)
        {
            int year = 0;
            var yearMatch = YearRe.Match(title ?? "");
            if (yearMatch.Success)
                int.TryParse(yearMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out year);

            string body = TagRe.Replace(title ?? "", "");
            int bracket = body.IndexOf('[');
            if (bracket >= 0)
                body = body[..bracket];

            string[] parts = body.Split(new[] { " / " }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();

            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                return ("", "", year);

            string name = parts[0];
            string original = "";
            for (int i = 1; i < parts.Length; i++)
            {
                if (HasLatin(parts[i]) && !HasCyrillic(parts[i]))
                {
                    original = parts[i];
                    break;
                }
            }

            return (name.Trim(), original.Trim(), year);
        }

        public static string CategoryTag(string title)
        {
            var m = TagRe.Match(title ?? "");
            return m.Success ? m.Groups[1].Value : "";
        }

        public static AnibelkaDetails BuildTorrent(
            string host, AnibelkaListingItem item, AnibelkaTopicInfo info, string magnet)
        {
            if (item == null || info == null)
                return null;

            var (name, original, year) = ParseTitle(item.Title);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(magnet))
                return null;

            return new AnibelkaDetails
            {
                trackerName = TrackerName,
                types = new[] { "anime" },
                url = TopicUrl(host, item.TopicId),
                title = item.Title,
                sid = info.Sid,
                pir = info.Pir,
                sizeName = info.SizeName,
                magnet = magnet,
                createTime = info.CreateTime == default ? DateTime.UtcNow : info.CreateTime,
                updateTime = DateTime.UtcNow,
                name = name,
                originalname = original,
                relased = year,
                downloadId = info.TorrentId
            };
        }

        /// <summary>Reads «23 июл 2026, 08:56» as Europe/Moscow and returns UTC.</summary>
        public static DateTime ParseRuDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return default;

            s = WhitespaceRe.Replace((s ?? "").Replace('\u00A0', ' ').Trim(), " ");
            var m = RuDateRe.Match(s);
            if (!m.Success)
                return default;

            if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)
                || !int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year))
            {
                return default;
            }

            string key = m.Groups[2].Value.ToLowerInvariant();
            if (key.Length > 3)
                key = key[..3];
            if (!RuMonths.TryGetValue(key, out int month))
                return default;

            int hour = 0, minute = 0;
            if (m.Groups[4].Success && !string.IsNullOrEmpty(m.Groups[4].Value))
            {
                int.TryParse(m.Groups[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour);
                int.TryParse(m.Groups[5].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minute);
            }

            try
            {
                var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTimeToUtc(local, MoscowTz);
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

        static string CleanText(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            s = StripTagsRe.Replace(s, "");
            s = WebUtility.HtmlDecode(s) ?? "";
            s = WhitespaceRe.Replace(s, " ");
            return s.Trim();
        }

        static bool HasCyrillic(string s)
        {
            foreach (char r in s ?? "")
            {
                if ((r >= 'А' && r <= 'я') || r == 'Ё' || r == 'ё')
                    return true;
            }

            return false;
        }

        static bool HasLatin(string s)
        {
            foreach (char r in s ?? "")
            {
                if ((r >= 'A' && r <= 'Z') || (r >= 'a' && r <= 'z'))
                    return true;
            }

            return false;
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

    public sealed class AnibelkaListingItem
    {
        public string TopicId { get; set; }
        public string Title { get; set; }
    }

    public sealed class AnibelkaTopicInfo
    {
        public string TorrentId { get; set; }
        public string SizeName { get; set; }
        public int Sid { get; set; }
        public int Pir { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
