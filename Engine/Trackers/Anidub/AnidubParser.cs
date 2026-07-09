using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Engine.Parsing;
using JacRed.Models.Details;

namespace JacRed.Engine.Trackers.Anidub
{
    public static class AnidubParser
    {
        const string TrackerName = "anidub";

        public const string ValidationDleContent = "dle-content";

        public static int ExtractRelased(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return 0;

            var yearMatch = Regex.Match(html, @"<b>Год:\s*</b>\s*<span>\s*<a[^>]*>([0-9]{4})</a>\s*</span>", RegexOptions.IgnoreCase);
            if (!yearMatch.Success)
            {
                yearMatch = Regex.Match(html, @"<b>Год:\s*</b>\s*<span>([0-9]{4})</span>", RegexOptions.IgnoreCase);
            }

            if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int year) && year > 1900 && year <= DateTime.UtcNow.Year + 1)
            {
                return year;
            }

            return 0;
        }

        public static List<AnidubDetails> ParseTorrentListFromHtml(string html, string host, int page)
        {
            var torrents = new List<AnidubDetails>();

            var rows = html.Contains("<article")
                ? tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html)).Split("<article").Skip(1)
                : tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html)).Split(new[] { "<div class=\"story", "<div class=\"rand", "<li><a href=\"" }, StringSplitOptions.None).Skip(1);

            foreach (string row in rows)
            {
                string Match(string pattern, int index = 1)
                {
                    string res = new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim();
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                if (!row.Contains("href=\"") || !row.Contains(".html"))
                    continue;

                DateTime createTime = default;

                string dateStr = Match("<li><b>Дата:</b> ([^<]+)</li>");
                if (!string.IsNullOrWhiteSpace(dateStr))
                {
                    if (dateStr.Contains("Сегодня"))
                    {
                        createTime = DateTime.UtcNow;
                    }
                    else if (dateStr.Contains("Вчера"))
                    {
                        createTime = DateTime.UtcNow.AddDays(-1);
                    }
                    else
                    {
                        var dateMatch = Regex.Match(dateStr, "([0-9]{1,2})-([0-9]{2})-([0-9]{4})");
                        if (dateMatch.Success)
                        {
                            string day = dateMatch.Groups[1].Value.PadLeft(2, '0');
                            string month = dateMatch.Groups[2].Value;
                            string year = dateMatch.Groups[3].Value;
                            createTime = tParse.ParseCreateTime($"{day}.{month}.{year}", "dd.MM.yyyy");
                        }
                    }
                }

                if (createTime == default)
                {
                    if (page != 1)
                        continue;
                    createTime = DateTime.UtcNow;
                }

                var gurl = Regex.Match(row, "<a href=\"([^\"]+)\"[^>]*>([^<]+)</a>");
                if (!gurl.Success)
                    continue;

                string urlPath = gurl.Groups[1].Value;
                string title = gurl.Groups[2].Value;

                if (string.IsNullOrWhiteSpace(urlPath) || string.IsNullOrWhiteSpace(title))
                    continue;

                if (urlPath.Contains("/user/") || urlPath.Contains("/xfsearch/") ||
                    urlPath.Contains("/forum/") || urlPath.Contains("javascript:") ||
                    urlPath.StartsWith("#") || !urlPath.Contains(".html"))
                    continue;

                string fullUrl = urlPath.StartsWith("http") ? urlPath : $"{host}/{urlPath.TrimStart('/')}";

                title = HttpUtility.HtmlDecode(title).Trim();
                title = Regex.Replace(title, "[\n\r\t ]+", " ").Trim();

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                string name = null, originalname = null;

                var nameMatch = Regex.Match(title, "^([^/]+)\\s*/\\s*([^\\[]+)(?:\\s*\\[|$)");
                if (nameMatch.Success)
                {
                    name = nameMatch.Groups[1].Value.Trim();
                    originalname = nameMatch.Groups[2].Value.Trim();
                }
                else
                {
                    var simpleMatch = Regex.Match(title, "^([^\\[]+)(?:\\s*\\[|$)");
                    if (simpleMatch.Success)
                    {
                        name = simpleMatch.Groups[1].Value.Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                string downloadUri = fullUrl;

                string[] types = new string[] { "anime" };

                if (urlPath.Contains("/dorama/"))
                    types = new string[] { "dorama" };
                else if (urlPath.Contains("/anime_movie/") || urlPath.Contains("/anime-movie/"))
                    types = new string[] { "anime", "movie" };
                else if (urlPath.Contains("/anime_ova/") || urlPath.Contains("/anime-ova/"))
                    types = new string[] { "anime", "ova" };
                else if (urlPath.Contains("/anime_tv/") || urlPath.Contains("/anime-tv/"))
                    types = new string[] { "anime", "serial" };

                torrents.Add(new AnidubDetails()
                {
                    trackerName = TrackerName,
                    types = types,
                    url = fullUrl,
                    title = title,
                    sid = 1,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    downloadUri = downloadUri
                });
            }

            return torrents;
        }
    }
}
