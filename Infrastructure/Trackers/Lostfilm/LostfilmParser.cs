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
    public static class LostfilmParser
    {
        /// <summary>Строит по HTML карту urlPath сериала (series/.../season_N/episode_M/) -> (name ru, originalname) из блоков hor-breaker, чтобы подставлять русское название в episode_links и избегать дубликатов бакетов. Добавляется и ключ по сериалу (series/Slug), чтобы все эпизоды одного сериала получали одно русское имя (Пони, а не Ponies).</summary>
        public static Dictionary<string, (string name, string originalname)> BuildHorBreakerNameMap(string html)
        {
            var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (string row in html.Split(new[] { "class=\"hor-breaker dashed\"" }, StringSplitOptions.None).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;
                string url = Regex.Match(row, @"href=""/([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string name = Regex.Match(row, @"<div class=""name-ru"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string originalname = Regex.Match(row, @"<div class=""name-en"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(url) || !url.StartsWith("series/") || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(originalname))
                    continue;
                string key = url.TrimEnd('/');
                var pair = (HttpUtility.HtmlDecode(name), HttpUtility.HtmlDecode(originalname));
                if (!map.ContainsKey(key))
                    map[key] = pair;
                // Ключ по сериалу (series/Slug), чтобы эпизоды, которых нет в hor-breaker на этой странице, тоже получили русское имя (например Пони вместо Ponies).
                var seriesMatch = Regex.Match(url, @"^series/([^/]+)(?:/|$)", RegexOptions.IgnoreCase);
                if (seriesMatch.Success)
                {
                    string seriesKey = "series/" + seriesMatch.Groups[1].Value.TrimEnd('/');
                    if (!map.ContainsKey(seriesKey))
                        map[seriesKey] = pair;
                }
            }
            return map;
        }

        /// <summary>Оставляет по одному торренту на url; при дубликате оставляет запись с русским названием (name != originalname), чтобы ключ бакета был один.</summary>
        public static void DedupeListByUrl(List<TorrentDetails> list)
        {
            var byUrl = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in list)
            {
                if (string.IsNullOrEmpty(t?.url))
                    continue;
                if (!byUrl.TryGetValue(t.url, out var existing))
                {
                    byUrl[t.url] = t;
                    continue;
                }
                // Уже есть запись: оставляем ту, у которой есть русское название (name != originalname)
                bool currentHasRu = !string.IsNullOrEmpty(t.name) && !string.IsNullOrEmpty(t.originalname) && !string.Equals(t.name, t.originalname, StringComparison.OrdinalIgnoreCase);
                bool existingHasRu = !string.IsNullOrEmpty(existing.name) && !string.IsNullOrEmpty(existing.originalname) && !string.Equals(existing.name, existing.originalname, StringComparison.OrdinalIgnoreCase);
                if (currentHasRu && !existingHasRu)
                    byUrl[t.url] = t;
            }
            list.Clear();
            list.AddRange(byUrl.Values);
        }

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

                // В блоке может быть несколько дат (год сериала и дата эпизода). Берём последнюю — она относится к текущему эпизоду (season_N/episode_N), а не к первому сезону.
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

        /// <summary>Извлекает год выхода и русское название из HTML страницы сериала или /seasons/. Без запросов — только парсинг.</summary>
        public static (int year, string russianName) ParseRelasedAndNameFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return (0, null);
            var m = Regex.Match(html, @"itemprop=""dateCreated""\s+content=""(\d{4})-\d{2}-\d{2}""");
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out int year) || year <= 0)
                return (0, null);
            string russianName = null;
            var og = Regex.Match(html, @"<meta\s+property=""og:title""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
            if (og.Success)
                russianName = HttpUtility.HtmlDecode(og.Groups[1].Value.Trim());
            if (string.IsNullOrWhiteSpace(russianName))
            {
                var tit = Regex.Match(html, @"<title>([^<]+?)\.?\s*[–-]\s*LostFilm", RegexOptions.IgnoreCase);
                if (tit.Success)
                    russianName = ShortenSeriesName(HttpUtility.HtmlDecode(tit.Groups[1].Value.Trim()));
            }
            else
                russianName = ShortenSeriesName(russianName);
            return (year, russianName);
        }

        /// <summary>Нормализует качество в единый формат: 1080/720 → 1080p/720p, SD без изменений.</summary>
        public static string NormalizeQuality(string quality)
        {
            if (string.IsNullOrWhiteSpace(quality))
                return quality;
            string q = quality.Trim();
            if (Regex.IsMatch(q, @"^\d{3,4}p$", RegexOptions.IgnoreCase))
                return q.ToLowerInvariant();
            if (string.Equals(q, "1080", StringComparison.OrdinalIgnoreCase))
                return "1080p";
            if (string.Equals(q, "720", StringComparison.OrdinalIgnoreCase))
                return "720p";
            if (string.Equals(q, "sd", StringComparison.OrdinalIgnoreCase))
                return "SD";
            if (string.Equals(q, "mp4", StringComparison.OrdinalIgnoreCase))
                return "720p";
            return q;
        }

        /// <summary>Извлекает короткое русское название сериала для полей name/title. og:title на LostFilm часто содержит длинный текст: "Название (English). Сериал ... гид по сериям... / OriginalName / N сезон M серия [year, 1080p]".</summary>
        public static string ShortenSeriesName(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return title?.Trim() ?? "";

            const int maxNameLength = 200;
            string s = title.Trim();

            // 1) og:title формат: "Название (English). Сериал Название (English) канал (страны): гид по сериям..." — берём до ". Сериал", затем до " (" (только русское название)
            int idxSer = s.IndexOf(". Сериал", StringComparison.OrdinalIgnoreCase);
            if (idxSer >= 0)
            {
                s = s.Substring(0, idxSer).Trim();
                int idxParen = s.IndexOf(" (", StringComparison.Ordinal);
                if (idxParen >= 0)
                    s = s.Substring(0, idxParen).Trim();
                if (s.Length > 0 && s.Length <= maxNameLength)
                    return s;
            }

            // 2) Уже в формате "Name RU / Name EN / N сезон M серия [year]" или "[year, 1080p]" — извлекаем первый сегмент (русское название)
            var m = Regex.Match(s, @"^(.+?)\s*/\s*[^/]+?\s*/\s*\d+\s*сезон\s*\d+\s*серия\s*\[\d{4}(?:,[^\]]*)?\]\s*$");
            if (m.Success)
            {
                s = m.Groups[1].Value.Trim();
                if (s.Length > 0 && s.Length <= maxNameLength)
                    return s;
            }

            // 3) Есть скобка " (Original Name)" — оставляем только русскую часть
            int idx = s.IndexOf(" (", StringComparison.Ordinal);
            if (idx >= 0)
                s = s.Substring(0, idx).Trim();

            if (s.Length > maxNameLength)
                s = s.Substring(0, maxNameLength).Trim();
            return s.Length > 0 ? s : title.Trim();
        }

        public static int ExtractTotalPagesFromNewPageHtml(string html)
        {
            int totalPages = 1;
            if (!string.IsNullOrEmpty(html) && html.Contains("LostFilm.TV"))
            {
                var pageMatches = Regex.Matches(html, @"/new/page_(\d+)");
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
            var sinfoRe = new Regex(@"(\d+)\s*сезон\s*(\d+)\s*серия", RegexOptions.IgnoreCase);
            var dateRe = new Regex(@"(\d{2}\.\d{2}\.\d{4})");

            // episode_links
            var linkRe = new Regex(@"<a\s[^>]*href=""[^""]*?(/series/([^/""]+)/season_(\d+)/episode_(\d+)/)[^""]*""[^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase);
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

            // new-movie blocks (другой формат даты в блоке)
            var newMovieRe = new Regex(@"<a\s+class=""new-movie""\s+href=""(?:https?://[^""]+)?(/series/[^""]+)""[^>]*title=""([^""]*)""[^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase);
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

            // hor-breaker
            foreach (string row in html.Split(new[] { "class=\"hor-breaker dashed\"" }, StringSplitOptions.None).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;
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

        /// <summary>Парсит HTML страницы InSearch (V/?c=...) и извлекает варианты качества с torrent-ссылками (без скачивания).</summary>
        public static List<(string torrentUrl, string quality)> ParseVPageQualityLinkUrls(string searchHtml)
        {
            if (string.IsNullOrEmpty(searchHtml) || !searchHtml.Contains("inner-box--link"))
                return new List<(string, string)>();

            string flat = Regex.Replace(searchHtml, @"[\n\r\t]+", " ");
            var linkRe = new Regex(@"<div\s+class=""inner-box--link\s+main""[^>]*><a\s+href=""([^""]+)""[^>]*>([^<]+)</a></div>", RegexOptions.IgnoreCase);
            var results = new List<(string torrentUrl, string quality)>();

            foreach (Match m in linkRe.Matches(flat))
            {
                string linkText = m.Groups[2].Value;
                string quality = Regex.Match(linkText, @"(2160p|2060p|1440p|1080p|720p)", RegexOptions.IgnoreCase).Groups[1].Value;
                if (string.IsNullOrEmpty(quality))
                    quality = Regex.Match(linkText, @"\b(1080|720)\b", RegexOptions.IgnoreCase).Groups[1].Value?.ToLowerInvariant();
                if (string.IsNullOrEmpty(quality) && linkText.IndexOf("MP4", StringComparison.OrdinalIgnoreCase) >= 0)
                    quality = "720p";
                if (string.IsNullOrEmpty(quality))
                    quality = Regex.Match(linkText, @"\bSD\b", RegexOptions.IgnoreCase).Success ? "SD" : null;
                if (string.IsNullOrEmpty(quality))
                    continue;
                quality = NormalizeQuality(quality);
                string torrentUrl = m.Groups[1].Value;
                if (string.IsNullOrEmpty(torrentUrl))
                    continue;
                results.Add((torrentUrl, quality));
            }
            return results;
        }
    }
}
