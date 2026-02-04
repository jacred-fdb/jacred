using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using JacRed.Engine;
using JacRed.Models.Details;
using System.Diagnostics;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/lostfilm/[action]")]
    public class LostfilmController : Controller
    {
        static readonly object _parseLock = new object();
        static bool _parseRunning;

        /// <summary>Парсит только первую страницу /new/ — актуальные новинки.</summary>
        [HttpGet]
        public async Task<string> Parse()
        {
            if (AppInit.conf == null || AppInit.conf.Lostfilm == null)
                return "conf";

            lock (_parseLock)
            {
                if (_parseRunning)
                    return "work";
                _parseRunning = true;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                string host = AppInit.conf.Lostfilm.host ?? "https://www.lostfilm.tv";
                string cookie = AppInit.conf.Lostfilm.cookie;

                ParserLog.Write("lostfilm", "Parse (page /new/) start");
                await ParsePage(host, cookie, 1, stopBeforeDate: null, startFromDate: null, preloadedHtml: null);
                ParserLog.Write("lostfilm", $"Parse done in {sw.Elapsed.TotalSeconds:F1}s");
                return "ok";
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"Parse error: {ex.Message}");
                throw;
            }
            finally
            {
                lock (_parseLock)
                    _parseRunning = false;
            }
        }

        /// <summary>Парсит страницы /new/ в указанном диапазоне. Без кэша, без фильтров по датам.</summary>
        /// <param name="pageFrom">Начальная страница (1 = /new/, 2 = /new/page_2, ...)</param>
        /// <param name="pageTo">Конечная страница включительно. Если больше реального числа страниц — обрезается.</param>
        [HttpGet]
        public async Task<string> ParsePages(int pageFrom = 1, int pageTo = 1)
        {
            if (AppInit.conf == null || AppInit.conf.Lostfilm == null)
                return "conf";

            lock (_parseLock)
            {
                if (_parseRunning)
                    return "work";
                _parseRunning = true;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                string host = AppInit.conf.Lostfilm.host ?? "https://www.lostfilm.tv";
                string cookie = AppInit.conf.Lostfilm.cookie;
                int delay = AppInit.conf.Lostfilm.parseDelay;

                if (pageFrom < 1) pageFrom = 1;
                if (pageTo < pageFrom) pageTo = pageFrom;

                ParserLog.Write("lostfilm", $"ParsePages start pageFrom={pageFrom} pageTo={pageTo} host={host}");

                int totalPages = 1;
                string firstPageHtml = await HttpClient.Get($"{host}/new/", cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy, httpversion: 2);
                if (!string.IsNullOrEmpty(firstPageHtml) && firstPageHtml.Contains("LostFilm.TV"))
                {
                    var pageMatches = Regex.Matches(firstPageHtml, @"/new/page_(\d+)");
                    for (int i = 0; i < pageMatches.Count; i++)
                        if (int.TryParse(pageMatches[i].Groups[1].Value, out int n) && n > totalPages)
                            totalPages = n;
                    if (totalPages > 100)
                        totalPages = 100;
                }
                if (pageTo > totalPages)
                    pageTo = totalPages;
                ParserLog.Write("lostfilm", $"Pagination: totalPages={totalPages} will parse pages {pageFrom}..{pageTo}");

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    if (page > 1 && delay > 0)
                        await Task.Delay(delay);

                    await ParsePage(host, cookie, page, stopBeforeDate: null, startFromDate: null, page == 1 ? firstPageHtml : null);
                }

                ParserLog.Write("lostfilm", $"ParsePages done in {sw.Elapsed.TotalSeconds:F1}s");
                return "ok";
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"ParsePages error: {ex.Message}");
                throw;
            }
            finally
            {
                lock (_parseLock)
                    _parseRunning = false;
            }
        }

        /// <summary>Парсит страницу /series/{series}/seasons/ и добавляет торренты «полный сезон» (SD, 1080p, 720p) для каждого сезона с e=999.</summary>
        /// <param name="series">Slug сериала, например Outer_Banks</param>
        [HttpGet]
        public async Task<string> ParseSeasonPacks(string series)
        {
            if (AppInit.conf == null || AppInit.conf.Lostfilm == null)
                return "conf";
            if (string.IsNullOrWhiteSpace(series))
                return "series required";

            lock (_parseLock)
            {
                if (_parseRunning)
                    return "work";
                _parseRunning = true;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                string host = AppInit.conf.Lostfilm.host ?? "https://www.lostfilm.tv";
                string cookie = AppInit.conf.Lostfilm.cookie;
                series = series.Trim();

                ParserLog.Write("lostfilm", $"ParseSeasonPacks start series={series}");

                string seasonsUrl = $"{host}/series/{series}/seasons/";
                string html = await HttpClient.Get(seasonsUrl, cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy);
                if (string.IsNullOrEmpty(html) || !html.Contains("LostFilm.TV"))
                {
                    ParserLog.Write("lostfilm", $"ParseSeasonPacks: empty or invalid response {seasonsUrl}");
                    return "empty";
                }

                var (relased, russianName) = ParseRelasedAndNameFromHtml(html);
                if (relased <= 0)
                {
                    ParserLog.Write("lostfilm", $"ParseSeasonPacks: no relased in HTML for {series}");
                    return "no relased";
                }
                string originalname = series.Replace("_", " ");
                string name = !string.IsNullOrWhiteSpace(russianName) ? russianName : originalname;

                // Ссылки на полный сезон: /V/?c=...&s=N&e=999 (или e=999&s=N)
                var vLinkRe = new Regex(@"href=""(/V/\?[^""]+)""", RegexOptions.IgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var list = new List<TorrentDetails>();

                foreach (Match m in vLinkRe.Matches(html))
                {
                    string vPath = m.Groups[1].Value;
                    if (vPath.IndexOf("e=999", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var sMatch = Regex.Match(vPath, @"[?&]s=(\d+)", RegexOptions.IgnoreCase);
                    if (!sMatch.Success || !int.TryParse(sMatch.Groups[1].Value, out int seasonNum) || seasonNum <= 0)
                        continue;
                    string vFullUrl = vPath.StartsWith("http") ? vPath : host.TrimEnd('/') + (vPath.StartsWith("/") ? vPath : "/" + vPath);
                    if (seen.Contains(vFullUrl))
                        continue;
                    seen.Add(vFullUrl);

                    var magnets = await GetMagnetsFromVPage(host, cookie, vFullUrl);
                    if (magnets.Count == 0)
                    {
                        ParserLog.Write("lostfilm", $"  no magnets: {series} s{seasonNum}");
                        continue;
                    }
                    DateTime createTime = DateTime.UtcNow;
                    foreach (var (magnet, quality, sizeName) in magnets)
                    {
                        string title = $"{name} / {originalname} / {seasonNum} сезон (полный сезон) [{relased}, {quality}]";
                        string url = vFullUrl + "#" + quality;
                        list.Add(new TorrentDetails
                        {
                            trackerName = "lostfilm",
                            types = new[] { "serial" },
                            url = url,
                            title = title,
                            sid = 1,
                            createTime = createTime,
                            name = name,
                            originalname = originalname,
                            relased = relased,
                            magnet = magnet,
                            sizeName = sizeName
                        });
                    }
                    ParserLog.Write("lostfilm", $"  + {name} {seasonNum} сезон (полный): {magnets.Count} quality");
                }

                if (list.Count > 0)
                {
                    await FileDB.AddOrUpdate(list, (t, db) => Task.FromResult(true));
                    ParserLog.Write("lostfilm", $"ParseSeasonPacks: added {list.Count} torrents");
                }
                else
                    ParserLog.Write("lostfilm", "ParseSeasonPacks: no season-pack links found");

                ParserLog.Write("lostfilm", $"ParseSeasonPacks done in {sw.Elapsed.TotalSeconds:F1}s");
                return "ok";
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"ParseSeasonPacks error: {ex.Message}");
                throw;
            }
            finally
            {
                lock (_parseLock)
                    _parseRunning = false;
            }
        }

        /// <returns>true если нужно прекратить парсинг (достигли stopBeforeDate)</returns>
        /// <param name="startFromDate">Если задано — в обработку попадают только раздачи с createTime &lt;= startFromDate</param>
        /// <param name="preloadedHtml">Если не null, использовать вместо GET (для первой страницы при пагинации)</param>
        static async Task<bool> ParsePage(string host, string cookie, int page, DateTime? stopBeforeDate, DateTime? startFromDate, string preloadedHtml = null)
        {
            string url = page > 1 ? $"{host}/new/page_{page}" : $"{host}/new/";
            string html = preloadedHtml;
            if (string.IsNullOrEmpty(html))
            {
                ParserLog.Write("lostfilm", $"Page {page}: GET {url}");
                html = await HttpClient.Get(url, cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy, httpversion: 2);
            }
            else
                ParserLog.Write("lostfilm", $"Page {page}: use preloaded");
            if (string.IsNullOrEmpty(html))
            {
                ParserLog.Write("lostfilm", $"Page {page}: empty response");
                return false;
            }
            if (!html.Contains("LostFilm.TV"))
            {
                ParserLog.Write("lostfilm", $"Page {page}: no 'LostFilm.TV' in response (cookies/redirect?)");
                return false;
            }

            string normalized = tParse.ReplaceBadNames(html);
            var list = new List<TorrentDetails>();

            // Карта urlPath -> (name, originalname) из hor-breaker: чтобы эпизоды из episode_links попадали в тот же бакет (Капли Бога : Drops of God), а не создавали дубликат (Drops of God : Drops of God).
            var horBreakerNameMap = BuildHorBreakerNameMap(normalized);

            await CollectFromEpisodeLinks(normalized, host, cookie, list, page, horBreakerNameMap);
            string source = "episode_links";
            if (list.Count == 0)
            {
                await CollectFromNewMovie(normalized, host, cookie, list, page);
                source = "new-movie";
            }
            if (list.Count == 0)
            {
                await CollectFromHorBreaker(normalized, host, cookie, list, page);
                source = "hor-breaker";
            }
            int beforeMovies = list.Count;
            await CollectFromMovies(normalized, host, cookie, list, page);

            // Один url — одна запись: убираем дубликаты по url, оставляем запись с русским названием (name != originalname), чтобы не было двух бакетов на один сериал.
            DedupeListByUrl(list);
            if (list.Count > beforeMovies)
                source = source + "+movies:" + (list.Count - beforeMovies);

            DateTime? oldestOnPage = list.Count > 0 ? list.Min(t => t.createTime) : (DateTime?)null;

            if (startFromDate.HasValue && list.Count > 0)
            {
                int before = list.Count;
                list = list.Where(t => t.createTime <= startFromDate.Value).ToList();
                if (list.Count < before)
                    ParserLog.Write("lostfilm", $"Page {page}: filtered by startFromDate {before} -> {list.Count}");
            }

            ParserLog.Write("lostfilm", $"Page {page}: collected {list.Count} items (source={source})");

            if (stopBeforeDate.HasValue && oldestOnPage.HasValue && oldestOnPage.Value <= stopBeforeDate.Value)
                return true;

            if (list.Count == 0)
                return false;

            int added = 0, fromCache = 0, noMagnet = 0;
            await FileDB.AddOrUpdate(list, async (t, db) =>
            {
                if (!string.IsNullOrEmpty(t.magnet))
                    return true;
                if (db.TryGetValue(t.url, out TorrentDetails cached) && !string.IsNullOrEmpty(cached.magnet))
                {
                    fromCache++;
                    t.magnet = cached.magnet;
                    t.title = cached.title;
                    t.sizeName = cached.sizeName ?? t.sizeName;
                    return true;
                }

                var mag = t.types != null && t.types.Contains("movie")
                    ? await GetMagnetForMovie(host, cookie, t.url)
                    : await GetMagnet(host, cookie, t.url);
                if (string.IsNullOrEmpty(mag.magnet))
                {
                    noMagnet++;
                    ParserLog.Write("lostfilm", $"  no magnet: {t.url}");
                    return false;
                }

                t.magnet = mag.magnet;
                t.sizeName = mag.sizeName;
                if (!string.IsNullOrEmpty(mag.quality))
                {
                    string quality = NormalizeQuality(mag.quality);
                    if (t.title != null && t.title.TrimEnd().EndsWith("]"))
                        t.title = t.title.TrimEnd().Substring(0, t.title.TrimEnd().Length - 1) + ", " + quality + "]";
                    else
                        t.title = (t.title ?? "") + " [" + quality + "]";
                }
                added++;
                ParserLog.Write("lostfilm", $"  + {t.title?.Substring(0, Math.Min(60, t.title?.Length ?? 0))}... [{mag.quality}]");
                return true;
            });
            ParserLog.Write("lostfilm", $"Page {page}: added={added} fromCache={fromCache} noMagnet={noMagnet}");
            return false;
        }

        /// <summary>Строит по HTML карту urlPath сериала (series/.../season_N/episode_M/) -> (name ru, originalname) из блоков hor-breaker, чтобы подставлять русское название в episode_links и избегать дубликатов бакетов.</summary>
        static Dictionary<string, (string name, string originalname)> BuildHorBreakerNameMap(string html)
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
                if (!map.ContainsKey(key))
                    map[key] = (HttpUtility.HtmlDecode(name), HttpUtility.HtmlDecode(originalname));
            }
            return map;
        }

        /// <summary>Оставляет по одному торренту на url; при дубликате оставляет запись с русским названием (name != originalname), чтобы ключ бакета был один.</summary>
        static void DedupeListByUrl(List<TorrentDetails> list)
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

        static Task CollectFromEpisodeLinks(string html, string host, string cookie, List<TorrentDetails> list, int page, Dictionary<string, (string name, string originalname)> horBreakerNameMap = null)
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
                if (horBreakerNameMap != null && horBreakerNameMap.TryGetValue(urlPath.TrimEnd('/'), out var ruNames))
                {
                    name = ruNames.name;
                    originalname = ruNames.originalname;
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

        static Task CollectFromNewMovie(string html, string host, string cookie, List<TorrentDetails> list, int page)
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

        static Task CollectFromHorBreaker(string html, string host, string cookie, List<TorrentDetails> list, int page)
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

        /// <summary>Собирает фильмы с /new/: ссылки на /movies/ и блоки с «Фильм» + дата. Для каждого получает V-страницу и добавляет раздачи по качествам (SD, 1080p, 720p).</summary>
        static async Task CollectFromMovies(string html, string host, string cookie, List<TorrentDetails> list, int page)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dateRe = new Regex(@"(\d{2}\.\d{2}\.\d{4})");

            foreach (string row in html.Split(new[] { "class=\"hor-breaker dashed\"" }, StringSplitOptions.None).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;
                string url = Regex.Match(row, @"href=""/([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(url) || !url.StartsWith("movies/"))
                    continue;
                string leftPart = Regex.Match(row, @"<div class=""left-part"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                if (leftPart.IndexOf("Фильм", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string name = Regex.Match(row, @"<div class=""name-ru"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string originalname = Regex.Match(row, @"<div class=""name-en"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string dateStr = Regex.Match(row, @"<div class=""right-part"">(\d{2}\.\d{2}\.\d{4})</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(originalname) || string.IsNullOrEmpty(dateStr))
                    continue;

                string moviePageUrl = $"{host.TrimEnd('/')}/{url.TrimStart('/')}";
                if (seen.Contains(moviePageUrl))
                    continue;
                seen.Add(moviePageUrl);

                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    relased = createTime.Year;

                string vPageUrl = await GetVUrlFromMoviePage(host, cookie, moviePageUrl);
                if (string.IsNullOrEmpty(vPageUrl))
                {
                    ParserLog.Write("lostfilm", $"  movie no V link: {name}");
                    continue;
                }

                var magnets = await GetMagnetsFromVPage(host, cookie, vPageUrl);
                if (magnets.Count == 0)
                {
                    ParserLog.Write("lostfilm", $"  movie no magnets: {name}");
                    continue;
                }

                string nameDec = HttpUtility.HtmlDecode(name);
                string originalnameDec = HttpUtility.HtmlDecode(originalname);
                foreach (var (magnet, quality, sizeName) in magnets)
                {
                    string q = NormalizeQuality(quality);
                    string title = $"{nameDec} / {originalnameDec} [Фильм, {relased}, {q}]";
                    list.Add(new TorrentDetails
                    {
                        trackerName = "lostfilm",
                        types = new[] { "movie" },
                        url = moviePageUrl + "#" + q,
                        title = title,
                        sid = 1,
                        createTime = createTime,
                        name = nameDec,
                        originalname = originalnameDec,
                        relased = relased,
                        magnet = magnet,
                        sizeName = sizeName ?? ""
                    });
                }
                ParserLog.Write("lostfilm", $"  + movie {nameDec} ({magnets.Count} quality)");
            }
        }

        /// <summary>Со страницы фильма /movies/Slug извлекает ссылку на InSearch /V/?c=... (или редирект через v_search).</summary>
        static async Task<string> GetVUrlFromMoviePage(string host, string cookie, string moviePageUrl)
        {
            try
            {
                string html = await HttpClient.Get(moviePageUrl, cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy);
                if (string.IsNullOrEmpty(html))
                    return null;
                var vMatch = Regex.Match(html, @"href=""(/V/\?[^""]+)""", RegexOptions.IgnoreCase);
                if (vMatch.Success)
                    return vMatch.Groups[1].Value.StartsWith("http") ? vMatch.Groups[1].Value : host.TrimEnd('/') + vMatch.Groups[1].Value;
                var playMatch = Regex.Match(html, @"Play(?:Movie|Episode)\s*\(\s*['""]?(\d+)['""]?\s*\)", RegexOptions.IgnoreCase);
                if (playMatch.Success)
                {
                    string id = playMatch.Groups[1].Value;
                    string searchHtml = await HttpClient.Get($"{host}/v_search.php?a={id}", cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy);
                    if (string.IsNullOrEmpty(searchHtml))
                        return null;
                    var mMeta = Regex.Match(searchHtml, @"(?:content=""[^""]*url\s*=\s*|location\.replace\s*\(\s*[""'])([^""]+)");
                    if (mMeta.Success)
                        return mMeta.Groups[1].Value.Trim().StartsWith("http") ? mMeta.Groups[1].Value.Trim() : host.TrimEnd('/') + mMeta.Groups[1].Value.Trim();
                    var hRef = Regex.Match(searchHtml, @"href=""(/V/\?[^""]+)""");
                    if (hRef.Success)
                        return host.TrimEnd('/') + hRef.Groups[1].Value;
                }
                return null;
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"  GetVUrlFromMoviePage: {ex.Message}");
                return null;
            }
        }

        /// <summary>Для фильма: получает V-URL со страницы фильма и возвращает первый доступный магнит (одно качество).</summary>
        static async Task<(string magnet, string quality, string sizeName)> GetMagnetForMovie(string host, string cookie, string movieUrl)
        {
            string vPageUrl = await GetVUrlFromMoviePage(host, cookie, movieUrl);
            if (string.IsNullOrEmpty(vPageUrl))
                return default;
            var list = await GetMagnetsFromVPage(host, cookie, vPageUrl);
            if (list.Count > 0)
                return list[0];
            return default;
        }

        /// <summary>Извлекает год выхода и русское название из HTML страницы сериала или /seasons/. Без запросов — только парсинг.</summary>
        static (int year, string russianName) ParseRelasedAndNameFromHtml(string html)
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

        /// <summary>Парсит HTML страницы InSearch (V/?c=...) и возвращает все варианты качества с магнитами.</summary>
        static async Task<List<(string magnet, string quality, string sizeName)>> ParseVPageQualityLinks(string host, string cookie, string searchHtml)
        {
            if (string.IsNullOrEmpty(searchHtml) || !searchHtml.Contains("inner-box--link"))
                return new List<(string, string, string)>();

            string flat = Regex.Replace(searchHtml, @"[\n\r\t]+", " ");
            var linkRe = new Regex(@"<div\s+class=""inner-box--link\s+main""[^>]*><a\s+href=""([^""]+)""[^>]*>([^<]+)</a></div>", RegexOptions.IgnoreCase);
            var results = new List<(string magnet, string quality, string sizeName)>();

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
                byte[] data = await HttpClient.Download(torrentUrl, cookie: cookie, referer: $"{host}/");
                if (data == null || data.Length == 0)
                    continue;
                string magnet = BencodeTo.Magnet(data);
                if (string.IsNullOrEmpty(magnet))
                    continue;
                string sizeName = BencodeTo.SizeName(data) ?? "";
                results.Add((magnet, quality, sizeName));
            }
            return results;
        }

        /// <summary>Загружает страницу V (по прямой ссылке или через v_search.php) и возвращает HTML с inner-box--link.</summary>
        static async Task<string> FetchVPageHtml(string host, string cookie, string vPageUrlOrNull, string episodeIdForSearch = null)
        {
            string searchHtml = null;
            if (!string.IsNullOrEmpty(episodeIdForSearch))
            {
                searchHtml = await HttpClient.Get($"{host}/v_search.php?a={episodeIdForSearch}", cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy);
                if (string.IsNullOrEmpty(searchHtml))
                    return null;
            }
            else if (!string.IsNullOrEmpty(vPageUrlOrNull))
            {
                string url = vPageUrlOrNull.StartsWith("http") ? vPageUrlOrNull : host.TrimEnd('/') + (vPageUrlOrNull.StartsWith("/") ? vPageUrlOrNull : "/" + vPageUrlOrNull);
                searchHtml = await HttpClient.Get(url, cookie: cookie, referer: $"{host}/", useproxy: AppInit.conf.Lostfilm.useproxy);
                if (string.IsNullOrEmpty(searchHtml))
                    return null;
            }
            else
                return null;

            if (searchHtml.Contains("inner-box--link"))
                return searchHtml;
            string vPageUrl = null;
            var mMeta = Regex.Match(searchHtml, @"(?:content=""[^""]*url\s*=\s*|location\.replace\s*\(\s*[""'])([^""]+)");
            if (mMeta.Success)
                vPageUrl = mMeta.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(vPageUrl))
                vPageUrl = Regex.Match(searchHtml, @"href=""(/V/\?[^""]+)""").Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(vPageUrl))
                return searchHtml;
            if (vPageUrl.StartsWith("/"))
                vPageUrl = host.TrimEnd('/') + vPageUrl;
            searchHtml = await HttpClient.Get(vPageUrl, cookie: cookie, referer: $"{host}/", useproxy: AppInit.conf.Lostfilm.useproxy) ?? "";
            return searchHtml;
        }

        static async Task<(string magnet, string quality, string sizeName)> GetMagnet(string host, string cookie, string episodeUrl)
        {
            try
            {
                string html = await HttpClient.Get(episodeUrl, cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy);
                if (string.IsNullOrEmpty(html))
                {
                    ParserLog.Write("lostfilm", $"      GetMagnet: empty episode page {episodeUrl}");
                    return default;
                }
                var epMatch = Regex.Match(html, @"PlayEpisode\s*\(\s*['""]?(\d+)['""]?\s*\)");
                if (!epMatch.Success)
                {
                    ParserLog.Write("lostfilm", $"      GetMagnet: no PlayEpisode in {episodeUrl}");
                    return default;
                }
                string episodeId = epMatch.Groups[1].Value;
                ParserLog.Write("lostfilm", $"      GetMagnet: episodeId={episodeId}");

                string searchHtml = await FetchVPageHtml(host, cookie, null, episodeId);
                if (string.IsNullOrEmpty(searchHtml) || !searchHtml.Contains("inner-box--link"))
                {
                    ParserLog.Write("lostfilm", $"      GetMagnet: no inner-box--link after V page");
                    return default;
                }
                var list = await ParseVPageQualityLinks(host, cookie, searchHtml);
                if (list.Count > 0)
                    return list[0];
                ParserLog.Write("lostfilm", $"      GetMagnet: no suitable quality link found");
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"      GetMagnet error: {ex.Message}");
            }
            return default;
        }

        /// <summary>По прямой ссылке на страницу V (например /V/?c=589&s=4&e=999) возвращает все качества (SD, 1080p, 720p) для полного сезона.</summary>
        static async Task<List<(string magnet, string quality, string sizeName)>> GetMagnetsFromVPage(string host, string cookie, string vPageUrl)
        {
            try
            {
                string searchHtml = await FetchVPageHtml(host, cookie, vPageUrl, null);
                if (string.IsNullOrEmpty(searchHtml) || !searchHtml.Contains("inner-box--link"))
                    return new List<(string, string, string)>();
                return await ParseVPageQualityLinks(host, cookie, searchHtml);
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"      GetMagnetsFromVPage error: {ex.Message}");
                return new List<(string, string, string)>();
            }
        }

        /// <summary>Нормализует качество в единый формат: 1080/720 → 1080p/720p, SD без изменений.</summary>
        static string NormalizeQuality(string quality)
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
        static string ShortenSeriesName(string title)
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

        /// <summary>Запрашивает /new/, парсит даты и возвращает, что мы извлекаем (dateStr, relased). Для проверки года в заголовках. Опционально ?series=slug фильтрует по сериалу (например Drops_of_God).</summary>
        [HttpGet]
        public async Task<IActionResult> VerifyPage(string series = null)
        {
            if (AppInit.conf == null || AppInit.conf.Lostfilm == null)
                return Json(new { error = "conf" });

            string host = AppInit.conf.Lostfilm.host ?? "https://www.lostfilm.tv";
            string cookie = AppInit.conf.Lostfilm.cookie;
            string url = $"{host}/new/";

            string html = await HttpClient.Get(url, cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy, httpversion: 2);
            if (string.IsNullOrEmpty(html) || !html.Contains("LostFilm.TV"))
                return Json(new { error = "empty", url });

            var items = ParseNewPageDates(html, host);
            string seriesFilter = null;
            if (!string.IsNullOrWhiteSpace(series))
            {
                seriesFilter = series.Trim().Replace(" ", "_");
                string seriesNorm = seriesFilter.Replace("_", " ").ToLowerInvariant();
                items = items.Where(i =>
                {
                    string u = (i.url ?? "").ToLowerInvariant();
                    string t = (i.title ?? "").ToLowerInvariant();
                    return u.Contains(seriesNorm.Replace(" ", "_")) || u.Contains(seriesNorm) || t.Contains(seriesNorm);
                }).ToList();
            }

            return Json(new
            {
                ok = true,
                url,
                filteredBy = seriesFilter,
                count = items.Count,
                items = items.Select(i => new
                {
                    i.title,
                    i.dateStr,
                    i.relased,
                    i.url,
                    i.source
                }).ToArray()
            });
        }

        /// <summary>Парсит HTML /new/ и возвращает список с dateStr (как на сайте), relased (год в заголовке), title, url, source.</summary>
        static List<(string title, string dateStr, int relased, string url, string source)> ParseNewPageDates(string html, string host)
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

        /// <summary>Статистика по раздачам lostfilm в базе: количество, с магнитом, примеры ключей.</summary>
        [HttpGet]
        public IActionResult Stats()
        {
            if (AppInit.conf == null || AppInit.conf.Lostfilm == null)
                return Json(new { error = "conf" });

            var keysWithLostfilm = new List<string>();
            int total = 0, withMagnet = 0;
            if (FileDB.masterDb != null)
            {
                foreach (var item in FileDB.masterDb.ToArray())
                {
                    try
                    {
                        foreach (var t in FileDB.OpenRead(item.Key, cache: false).Values)
                        {
                            if (t.trackerName != "lostfilm")
                                continue;
                            total++;
                            if (!string.IsNullOrEmpty(t.magnet))
                                withMagnet++;
                            if (!keysWithLostfilm.Contains(item.Key))
                                keysWithLostfilm.Add(item.Key);
                        }
                    }
                    catch { }
                }
            }
            keysWithLostfilm.Sort();
            return Json(new
            {
                total,
                withMagnet,
                withoutMagnet = total - withMagnet,
                keysCount = keysWithLostfilm.Count,
                keys = keysWithLostfilm.Take(50).ToArray(),
                keysMore = keysWithLostfilm.Count > 50 ? keysWithLostfilm.Count - 50 : 0
            });
        }
    }
}
