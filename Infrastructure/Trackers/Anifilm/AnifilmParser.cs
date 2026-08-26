using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Anifilm
{
    public static class AnifilmParser
    {
        public const string TrackerName = "anifilm";

        public const string ValidationMarker = "AniFilm";

        static readonly Regex ItemSplitRe = new(
            @"class=""releases__item",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex UrlRe = new(
            @"<a[^>]+href=""/(releases/[^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex NameRuRe = new(
            @"class=""releases__title-russian""[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex NameOrigRe = new(
            @"class=""releases__title-original""[^>]*>([^<]+)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex EpisodesRe = new(
            @"([0-9]+(-[0-9]+)?)\s*из\s*[0-9]+\s*эп",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex YearRe = new(
            @"href=""/releases/[^""]*"">([0-9]{4})</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex YearAltRe = new(
            @"table-list__value[^>]*>[^<]*(\d{4})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex TidRe = new(
            @"href=""/(releases/download-torrent/[0-9]+)""[^>]*>скачать</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex CleanSpaceRe = new(@"[\n\r\t ]+", RegexOptions.Compiled);

        public static List<AnifilmDetails> ParseListingHtml(string body, string host, string[] types, DateTime createTime)
        {
            var outList = new List<AnifilmDetails>();
            if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(host) || types == null || types.Length == 0)
                return outList;

            if (!body.Contains(ValidationMarker, StringComparison.Ordinal))
                return outList;

            host = host.TrimEnd('/');
            string[] chunks = ItemSplitRe.Split(body);
            if (chunks.Length < 2)
                return outList;

            DateTime now = DateTime.UtcNow;

            for (int i = 1; i < chunks.Length; i++)
            {
                string row = chunks[i];
                if (string.IsNullOrWhiteSpace(row))
                    continue;

                string urlPath = Extract(UrlRe, row);
                string name = Extract(NameRuRe, row);
                string originalname = Extract(NameOrigRe, row);
                string episodes = Extract(EpisodesRe, row);

                if (string.IsNullOrWhiteSpace(urlPath) || string.IsNullOrWhiteSpace(name))
                    continue;

                if (string.IsNullOrWhiteSpace(originalname))
                    originalname = name;

                string fullUrl = host + "/" + urlPath.TrimStart('/');
                string title = name;
                if (!string.Equals(originalname, name, StringComparison.Ordinal))
                    title = name + " / " + originalname;
                if (!string.IsNullOrWhiteSpace(episodes))
                    title += " (" + episodes + ")";

                int paren = name.IndexOf('(');
                if (paren > 0)
                    name = name[..paren].Trim();

                string yearStr = Extract(YearRe, row);
                if (string.IsNullOrWhiteSpace(yearStr))
                    yearStr = Extract(YearAltRe, row);
                int.TryParse(yearStr, out int relased);

                outList.Add(new AnifilmDetails
                {
                    trackerName = TrackerName,
                    types = types,
                    url = fullUrl,
                    title = title,
                    sid = 1,
                    pir = 0,
                    createTime = createTime,
                    updateTime = now,
                    name = name,
                    originalname = originalname,
                    relased = relased
                });
            }

            return outList;
        }

        /// <summary>
        /// Prefer a 1080p torrent block; otherwise first download-torrent link.
        /// Returns relative path (releases/download-torrent/N) and whether 1080p was selected.
        /// </summary>
        public static (string tid, bool is1080p) ExtractTorrentDownloadPath(string detailHtml)
        {
            if (string.IsNullOrWhiteSpace(detailHtml))
                return (null, false);

            string[] blocks = detailHtml.Split("<li class=\"release__torrents-item\">", StringSplitOptions.None);
            foreach (string block in blocks)
            {
                if (!block.Contains("1080p", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!block.Contains("href=\"/releases/download-torrent/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var m = TidRe.Match(block);
                if (m.Success)
                    return (m.Groups[1].Value, true);
            }

            var fallback = TidRe.Match(detailHtml);
            if (fallback.Success)
                return (fallback.Groups[1].Value, false);

            return (null, false);
        }

        static string Extract(Regex re, string row)
        {
            var m = re.Match(row ?? "");
            if (!m.Success || m.Groups.Count < 2)
                return "";

            string s = WebUtility.HtmlDecode(m.Groups[1].Value)?.Trim() ?? "";
            s = CleanSpaceRe.Replace(s, " ");
            return s.Trim();
        }
    }
}
