using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Anistar
{
    public static class AnistarParser
    {
        const string TrackerName = "anistar";

        static readonly Regex PostUrlAbsRe = new Regex(@"https?://[^""'>]+/\d{2,}-[^""'>]+?\.html", RegexOptions.Compiled);
        static readonly Regex PostUrlRelRe = new Regex(@"/\d{2,}-[^""'>]+?\.html", RegexOptions.Compiled);
        static readonly Regex PageNumRe = new Regex(@"/page/([0-9]+)/", RegexOptions.Compiled);
        static readonly Regex H1Re = new Regex(@"<h1[^>]*>\s*(.*?)\s*</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex TorrentBlockRe = new Regex(@"<div id=""torrent_(\d+)_info""\s+class=""torrent""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex InfoD1Re = new Regex(@"<div class=""info_d1"">\s*([^<]+?)\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex DateRe = new Regex(@"\b(\d{2})-(\d{2})-(\d{4})\b", RegexOptions.Compiled);
        static readonly Regex SidRe = new Regex(@"<div class=""li_distribute"">\s*([0-9]+)\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex PirRe = new Regex(@"<div class=""li_swing"">\s*([0-9]+)\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex SeriesRangeRe = new Regex(@"сери[яи]\s+(\d{1,4})\s*-\s*(\d{1,4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex SeriesSingleRe = new Regex(@"серия\s+(\d{1,4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex FilmRe = new Regex(@"^\s*фильм\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex CleanSpaceRe = new Regex(@"\s+", RegexOptions.Compiled);

        public static int DetectLastPage(string listHtml)
        {
            if (string.IsNullOrWhiteSpace(listHtml))
                return 1;

            int maxPage = 1;
            foreach (Match m in PageNumRe.Matches(listHtml))
            {
                if (int.TryParse(m.Groups[1].Value, out int n) && n > maxPage)
                    maxPage = n;
            }
            return maxPage;
        }

        public static List<string> ExtractPostUrls(string listHtml, string host)
        {
            var outList = new List<string>();
            if (string.IsNullOrWhiteSpace(listHtml))
                return outList;

            host = (host ?? "").TrimEnd('/');
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in PostUrlAbsRe.Matches(listHtml))
            {
                string url = m.Value;
                if (seen.Add(url))
                    outList.Add(url);
            }

            foreach (Match m in PostUrlRelRe.Matches(listHtml))
            {
                string abs = host + m.Value;
                if (seen.Add(abs))
                    outList.Add(abs);
            }

            return outList;
        }

        public static (string name, string originalname) ParseTitleNames(string h1)
        {
            if (string.IsNullOrWhiteSpace(h1))
                return (null, null);

            h1 = CleanSpaceRe.Replace(HttpUtility.HtmlDecode(h1).Trim(), " ").Trim();
            string name = h1;
            string original = "";

            int sep = h1.IndexOf(" / ", StringComparison.Ordinal);
            if (sep >= 0)
            {
                name = h1.Substring(0, sep).Trim();
                original = h1.Substring(sep + 3).Trim();
            }

            if (string.IsNullOrWhiteSpace(name))
                return (null, null);

            return (name, original);
        }

        /// <summary>
        /// Episode/film label from <c>info_d1</c>. Does not treat the size number
        /// in <c>Фильм (3.05 Gb)</c> as an episode.
        /// </summary>
        public static (string epLabel, string epNum) ParseEpisodeLabel(string info)
        {
            if (string.IsNullOrWhiteSpace(info))
                return ("Серия 1", "1");

            info = info.Trim();
            var rangeMatch = SeriesRangeRe.Match(info);
            if (rangeMatch.Success)
                return ($"Серии {rangeMatch.Groups[1].Value}-{rangeMatch.Groups[2].Value}", rangeMatch.Groups[1].Value);

            var singleMatch = SeriesSingleRe.Match(info);
            if (singleMatch.Success)
                return ("Серия " + singleMatch.Groups[1].Value, singleMatch.Groups[1].Value);

            if (FilmRe.IsMatch(info))
                return ("Фильм", "film");

            return ("Серия 1", "1");
        }

        public static List<AnistarDetails> ParseDetailTorrents(string postHtml, string postUrl, string[] types)
        {
            var torrents = new List<AnistarDetails>();
            if (string.IsNullOrWhiteSpace(postHtml) || string.IsNullOrWhiteSpace(postUrl))
                return torrents;

            string h1 = "";
            var h1Match = H1Re.Match(postHtml);
            if (h1Match.Success)
                h1 = CleanSpaceRe.Replace(HttpUtility.HtmlDecode(h1Match.Groups[1].Value).Trim(), " ").Trim();

            var (name, original) = ParseTitleNames(h1);
            if (string.IsNullOrWhiteSpace(name))
                return torrents;

            string titleBase = string.IsNullOrWhiteSpace(original) ? name : $"{name} / {original}";

            foreach (Match bm in TorrentBlockRe.Matches(postHtml))
            {
                string tid = bm.Groups[1].Value;
                int startIdx = bm.Index;
                int endIdx = Math.Min(postHtml.Length, startIdx + 4000);
                string around = postHtml.Substring(startIdx, endIdx - startIdx);

                string epLabel = "Серия 1";
                string epNum = "1";
                var infoMatch = InfoD1Re.Match(around);
                if (infoMatch.Success)
                    (epLabel, epNum) = ParseEpisodeLabel(HttpUtility.HtmlDecode(infoMatch.Groups[1].Value));

                DateTime createTime = DateTime.UtcNow;
                int relased = createTime.Year;
                var dateMatch = DateRe.Match(around);
                if (dateMatch.Success
                    && int.TryParse(dateMatch.Groups[1].Value, out int day)
                    && int.TryParse(dateMatch.Groups[2].Value, out int month)
                    && int.TryParse(dateMatch.Groups[3].Value, out int year)
                    && day > 0 && month > 0 && year > 0)
                {
                    try
                    {
                        createTime = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
                        relased = year;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // keep defaults
                    }
                }

                int sid = 0, pir = 0;
                var sidMatch = SidRe.Match(around);
                if (sidMatch.Success)
                    int.TryParse(sidMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out sid);
                var pirMatch = PirRe.Match(around);
                if (pirMatch.Success)
                    int.TryParse(pirMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pir);

                torrents.Add(new AnistarDetails
                {
                    trackerName = TrackerName,
                    types = types,
                    url = $"{postUrl}?e={epNum}&id={tid}",
                    title = $"{titleBase} — {epLabel}",
                    sid = sid,
                    pir = pir,
                    createTime = createTime,
                    name = name,
                    originalname = string.IsNullOrWhiteSpace(original) ? name : original,
                    relased = relased,
                    downloadId = tid
                });
            }

            return torrents;
        }
    }
}
