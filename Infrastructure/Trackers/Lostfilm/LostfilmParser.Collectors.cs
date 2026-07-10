using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Lostfilm
{
    public static partial class LostfilmParser
    {
        public static Task CollectFromEpisodeLinks(string html, string host, string cookie, List<TorrentDetails> list, int page, Dictionary<string, (string name, string originalname)> horBreakerNameMap = null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var linkRe = new Regex(@"<a\s[^>]*href=""[^""]*?(/series/([^/""]+)/season_(\d+)/episode_(\d+)/)[^""]*""[^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase);
            var sinfoRe = new Regex(@"(\d+)\s*сезон\s*(\d+)\s*серия", RegexOptions.IgnoreCase);
            var dateRe = new Regex(@"(\d{2}\.\d{2}\.\d{4})");

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

                string dateStr = dateMatches[dateMatches.Count - 1].Groups[1].Value;
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;
                seen.Add(urlPath);
                string sinfo = HttpUtility.HtmlDecode(Regex.Replace(sm.Value, @"[\s]+", " ").Trim());
                string originalname = serieName.Replace("_", " ");
                string name = originalname;
                if (horBreakerNameMap != null)
                {
                    if (horBreakerNameMap.TryGetValue(urlPath.TrimEnd('/'), out var ruNames)
                        || horBreakerNameMap.TryGetValue("series/" + serieName, out ruNames))
                    {
                        name = ruNames.name;
                        originalname = ruNames.originalname;
                    }
                }
                list.Add(new TorrentDetails
                {
                    trackerName = "lostfilm",
                    types = new[] { "serial" },
                    url = $"{host}/{urlPath}",
                    title = $"{name} / {originalname} / {sinfo} [{relased}]",
                    sid = 1,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    relased = relased
                });
            }
            return Task.CompletedTask;
        }

        public static Task CollectFromNewMovie(string html, string host, string cookie, List<TorrentDetails> list, int page)
        {
            var re = new Regex(@"<a\s+class=""new-movie""\s+href=""(?:https?://[^""]+)?(/series/[^""]+)""[^>]*title=""([^""]*)""[^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase);
            foreach (Match m in re.Matches(html))
            {
                string urlPath = m.Groups[1].Value.TrimStart('/');
                string nameFromAttr = ShortenSeriesName(HttpUtility.HtmlDecode(m.Groups[2].Value.Trim()));
                string block = m.Groups[3].Value;
                if (string.IsNullOrEmpty(urlPath) || !urlPath.StartsWith("series/") || string.IsNullOrEmpty(nameFromAttr))
                    continue;

                string sinfo = Regex.Match(block, @"<div\s+class=""title""[^>]*>\s*([^<]+)\s*</div>", RegexOptions.IgnoreCase).Groups[1].Value;
                sinfo = HttpUtility.HtmlDecode(Regex.Replace(sinfo, @"[\s]+", " ").Trim());
                var newMovieDateMatches = Regex.Matches(block, @"<div\s+class=""date""[^>]*>(\d{2}\.\d{2}\.\d{4})</div>", RegexOptions.IgnoreCase);
                string dateStr = newMovieDateMatches.Count > 0 ? newMovieDateMatches[newMovieDateMatches.Count - 1].Groups[1].Value : "";
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default && page != 1)
                    continue;
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;

                string serieName = Regex.Match(urlPath, @"series/([^/]+)(?:/|$)").Groups[1].Value;
                if (string.IsNullOrEmpty(serieName))
                    continue;
                string originalname = serieName.Replace("_", " ");
                string seriesName = !string.IsNullOrWhiteSpace(nameFromAttr) ? nameFromAttr : originalname;
                list.Add(new TorrentDetails
                {
                    trackerName = "lostfilm",
                    types = new[] { "serial" },
                    url = $"{host}/{urlPath}",
                    title = $"{seriesName} / {originalname} / {sinfo} [{relased}]",
                    sid = 1,
                    createTime = createTime,
                    name = seriesName,
                    originalname = originalname,
                    relased = relased
                });
            }
            return Task.CompletedTask;
        }

        public static Task CollectFromHorBreaker(string html, string host, string cookie, List<TorrentDetails> list, int page)
        {
            foreach (string row in html.Split(new[] { "class=\"hor-breaker dashed\"" }, StringSplitOptions.None).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;
                string url = Regex.Match(row, @"href=""/([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string sinfo = Regex.Match(row, @"<div class=""left-part"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string name = Regex.Match(row, @"<div class=""name-ru"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string originalname = Regex.Match(row, @"<div class=""name-en"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string dateStr = Regex.Match(row, @"<div class=""right-part"">(\d{2}\.\d{2}\.\d{4})</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(url) || !url.StartsWith("series/") || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(originalname) || string.IsNullOrEmpty(sinfo))
                    continue;

                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default && page != 1)
                    continue;
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;

                string serieName = Regex.Match(url, @"series/([^/]+)(?:/|$)").Groups[1].Value;
                if (string.IsNullOrEmpty(serieName))
                    continue;
                list.Add(new TorrentDetails
                {
                    trackerName = "lostfilm",
                    types = new[] { "serial" },
                    url = $"{host}/{url}",
                    title = $"{name} / {originalname} / {sinfo} [{relased}]",
                    sid = 1,
                    createTime = createTime,
                    name = HttpUtility.HtmlDecode(name),
                    originalname = HttpUtility.HtmlDecode(originalname),
                    relased = relased
                });
            }
            return Task.CompletedTask;
        }
    }
}
