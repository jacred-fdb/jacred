using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;

namespace JacRed.Infrastructure.Trackers.Lostfilm
{
    public static partial class LostfilmParser
    {
        public static int ExtractTotalPagesFromNewPageHtml(string html)
        {
            int totalPages = 1;
            if (!string.IsNullOrEmpty(html) && html.Contains("LostFilm.TV"))
            {
                var pageMatches = NewPageNumRe.Matches(html);
                for (int i = 0; i < pageMatches.Count; i++)
                    if (int.TryParse(pageMatches[i].Groups[1].Value, out int n) && n > totalPages)
                        totalPages = n;
                if (totalPages > 100)
                    totalPages = 100;
            }
            return totalPages;
        }

        /// <summary>Парсит HTML /new/ и возвращает список с dateStr (как на сайте), relased (год в заголовке), title, url, source.</summary>
        public static List<(string title, string dateStr, int relased, string url, string source)> ParseNewPageDates(string html, string host)
        {
            var result = new List<(string title, string dateStr, int relased, string url, string source)>();
            var sinfoRe = SeasonEpisodeInfoRe;
            var dateRe = DateDdMmYyyyRe;

            var linkRe = EpisodeLinkRe;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in linkRe.Matches(html))
            {
                string urlPath = m.Groups[1].Value.TrimStart('/');
                string serieName = m.Groups[2].Value;
                string block = m.Groups[5].Value;
                if (string.IsNullOrEmpty(serieName) || seen.Contains(urlPath))
                    continue;
                var sm = sinfoRe.Match(block);
                var dateMatches = dateRe.Matches(block);
                if (!sm.Success || dateMatches.Count == 0)
                    continue;
                string sinfo = HttpUtility.HtmlDecode(Regex.Replace(sm.Value, @"[\s]+", " ").Trim());
                string dateStr = dateMatches[dateMatches.Count - 1].Groups[1].Value;
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;
                seen.Add(urlPath);
                string originalname = serieName.Replace("_", " ");
                string name = originalname;
                string title = $"{name} / {originalname} / {sinfo} [{relased}]";
                result.Add((title, dateStr, relased, $"{host?.TrimEnd('/')}/{urlPath}", "episode_links"));
            }

            var newMovieRe = NewMovieLinkRe;
            foreach (Match m in newMovieRe.Matches(html))
            {
                string urlPath = m.Groups[1].Value.TrimStart('/');
                string nameFromAttr = ShortenSeriesName(HttpUtility.HtmlDecode(m.Groups[2].Value.Trim()));
                string block = m.Groups[3].Value;
                if (string.IsNullOrEmpty(urlPath) || !urlPath.StartsWith("series/"))
                    continue;
                string sinfo = Regex.Match(block, @"<div\s+class=""title""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase).Groups[1].Value;
                sinfo = HttpUtility.HtmlDecode(Regex.Replace(sinfo, @"[\s]+", " ").Trim());
                var newMovieDateMatches = Regex.Matches(block, @"<div\s+class=""date""[^>]*>(\d{2}\.\d{2}\.\d{4})</div>", RegexOptions.IgnoreCase);
                string dateStr = newMovieDateMatches.Count > 0 ? newMovieDateMatches[newMovieDateMatches.Count - 1].Groups[1].Value : "";
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;
                string serieName = Regex.Match(urlPath, @"series/([^/]+)(?:/|$)").Groups[1].Value;
                string originalname = serieName.Replace("_", " ");
                string seriesName = !string.IsNullOrWhiteSpace(nameFromAttr) ? nameFromAttr : originalname;
                string title = $"{seriesName} / {originalname} / {sinfo} [{relased}]";
                string fullUrl = $"{host?.TrimEnd('/')}/{urlPath}";
                if (!result.Any(i => i.url == fullUrl))
                    result.Add((title, dateStr, relased, fullUrl, "new-movie"));
            }

            foreach (string row in html.Split(new[] { "class=\"hor-breaker dashed\"" }, StringSplitOptions.None).Skip(1).Where(row => !string.IsNullOrWhiteSpace(row)))
            {
                string url = Regex.Match(row, @"href=""/([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string sinfo = Regex.Match(row, @"<div class=""left-part"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string name = Regex.Match(row, @"<div class=""name-ru"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string originalname = Regex.Match(row, @"<div class=""name-en"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string dateStr = Regex.Match(row, @"<div class=""right-part"">(\d{2}\.\d{2}\.\d{4})</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(url) || !url.StartsWith("series/") || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(originalname) || string.IsNullOrEmpty(sinfo) || string.IsNullOrEmpty(dateStr))
                    continue;
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;
                string title = $"{HttpUtility.HtmlDecode(name)} / {HttpUtility.HtmlDecode(originalname)} / {sinfo} [{relased}]";
                string fullUrl = $"{host?.TrimEnd('/')}/{url}";
                if (!result.Any(i => i.url == fullUrl))
                    result.Add((title, dateStr, relased, fullUrl, "hor-breaker"));
            }

            return result;
        }
    }
}
