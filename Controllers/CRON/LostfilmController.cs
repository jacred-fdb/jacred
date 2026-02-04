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
                bool _ = await ParsePage(host, cookie, 1, stopBeforeDate: null, startFromDate: null, preloadedHtml: null);
                CleanupTempCache();
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

        /// <summary>Парсит устаревшие страницы /new/page_N с пагинацией и фильтрами по датам.</summary>
        /// <param name="maxpage">Макс. страниц (0 = по пагинации, но не более 100)</param>
        /// <param name="stopBeforeDate">Остановиться, когда встретится раздача с датой &lt;= этой (dd.MM.yyyy)</param>
        /// <param name="startFromDate">Не добавлять раздачи новее этой даты (dd.MM.yyyy) — только «старее или равно»</param>
        [HttpGet]
        public async Task<string> ParseOutdated(int maxpage = 20, string stopBeforeDate = null, string startFromDate = null)
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

                DateTime? stopDate = null;
                if (!string.IsNullOrWhiteSpace(stopBeforeDate) && tParse.ParseCreateTime(stopBeforeDate.Trim(), "dd.MM.yyyy") != default)
                    stopDate = tParse.ParseCreateTime(stopBeforeDate.Trim(), "dd.MM.yyyy");
                DateTime? fromDate = null;
                if (!string.IsNullOrWhiteSpace(startFromDate) && tParse.ParseCreateTime(startFromDate.Trim(), "dd.MM.yyyy") != default)
                    fromDate = tParse.ParseCreateTime(startFromDate.Trim(), "dd.MM.yyyy");

                ParserLog.Write("lostfilm", $"ParseOutdated start maxpage={maxpage} stopBeforeDate={stopBeforeDate} startFromDate={startFromDate} host={host}");

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
                    if (maxpage == 0)
                        maxpage = totalPages;
                    else if (maxpage > totalPages)
                        maxpage = totalPages;
                    ParserLog.Write("lostfilm", $"Pagination: totalPages={totalPages} will parse up to {maxpage}");
                }
                if (maxpage < 1)
                    maxpage = 1;

                for (int page = 1; page <= maxpage; page++)
                {
                    if (page > 1 && delay > 0)
                        await Task.Delay(delay);

                    bool stop = await ParsePage(host, cookie, page, stopBeforeDate: stopDate, startFromDate: fromDate, page == 1 ? firstPageHtml : null);
                    if (stop)
                    {
                        ParserLog.Write("lostfilm", $"Stopped at page {page} (reached stopBeforeDate)");
                        break;
                    }
                }

                CleanupTempCache();
                ParserLog.Write("lostfilm", $"ParseOutdated done in {sw.Elapsed.TotalSeconds:F1}s");
                return "ok";
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"ParseOutdated error: {ex.Message}");
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

            await CollectFromEpisodeLinks(normalized, host, cookie, list, page);
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
                if (db.TryGetValue(t.url, out TorrentDetails cached) && !string.IsNullOrEmpty(cached.magnet))
                {
                    fromCache++;
                    t.magnet = cached.magnet;
                    t.title = cached.title;
                    t.sizeName = cached.sizeName ?? t.sizeName;
                    return true;
                }

                var mag = await GetMagnet(host, cookie, t.url);
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

        static async Task CollectFromEpisodeLinks(string html, string host, string cookie, List<TorrentDetails> list, int page)
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
                var dm = dateRe.Match(block);
                if (!sm.Success || !dm.Success)
                    continue;

                string sinfo = HttpUtility.HtmlDecode(Regex.Replace(sm.Value, @"[\s]+", " ").Trim());
                string dateStr = dm.Groups[1].Value;
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;

                var (relased, russianName) = await GetRelasedAndName(host, cookie, serieName);
                if (relased <= 0)
                    continue;

                seen.Add(urlPath);
                string originalname = serieName.Replace("_", " ");
                string name = !string.IsNullOrWhiteSpace(russianName) ? russianName : originalname;
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
        }

        static async Task CollectFromNewMovie(string html, string host, string cookie, List<TorrentDetails> list, int page)
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
                string dateStr = Regex.Match(block, @"<div\s+class=""date""[^>]*>(\d{2}\.\d{2}\.\d{4})</div>", RegexOptions.IgnoreCase).Groups[1].Value;
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default && page != 1)
                    continue;
                if (createTime == default)
                    createTime = DateTime.UtcNow;

                string serieName = Regex.Match(urlPath, @"series/([^/]+)(?:/|$)").Groups[1].Value;
                if (string.IsNullOrEmpty(serieName))
                    continue;

                var (relased, russianName) = await GetRelasedAndName(host, cookie, serieName);
                if (relased <= 0)
                    continue;

                string originalname = serieName.Replace("_", " ");
                string seriesName = !string.IsNullOrWhiteSpace(russianName) ? russianName : nameFromAttr;
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
        }

        static async Task CollectFromHorBreaker(string html, string host, string cookie, List<TorrentDetails> list, int page)
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

                string serieName = Regex.Match(url, @"series/([^/]+)(?:/|$)").Groups[1].Value;
                if (string.IsNullOrEmpty(serieName))
                    continue;

                var (relased, _) = await GetRelasedAndName(host, cookie, serieName);
                if (relased <= 0)
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
        }

        /// <summary>Очистка кэша Data/temp/lostfilm: удаляем файлы старше 7 дней и при необходимости ограничиваем число файлов.</summary>
        static void CleanupTempCache()
        {
            const string cacheDir = "Data/temp/lostfilm";
            const int maxAgeDays = 7;
            const int maxFiles = 2500;
            const int keepFiles = 800;

            try
            {
                if (!System.IO.Directory.Exists(cacheDir))
                    return;

                var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
                var files = System.IO.Directory.GetFiles(cacheDir);
                int deleted = 0;

                foreach (var path in files)
                {
                    try
                    {
                        var fi = new System.IO.FileInfo(path);
                        if (fi.LastWriteTimeUtc < cutoff)
                        {
                            fi.Delete();
                            deleted++;
                        }
                    }
                    catch { /* один файл не удалился — не критично */ }
                }

                if (files.Length - deleted > maxFiles)
                {
                    var byAge = new System.IO.DirectoryInfo(cacheDir)
                        .GetFiles()
                        .OrderBy(f => f.LastWriteTimeUtc)
                        .ToList();
                    foreach (var fi in byAge.Take(Math.Max(0, byAge.Count - keepFiles)))
                    {
                        try
                        {
                            fi.Delete();
                            deleted++;
                        }
                        catch { }
                    }
                }

                if (deleted > 0)
                    ParserLog.Write("lostfilm", $"CleanupTempCache: removed {deleted} file(s) from {cacheDir}");
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"CleanupTempCache: {ex.Message}");
            }
        }

        /// <summary>Год выхода и русское название сериала (для поиска по русскому). Кэш: .relased и .name</summary>
        static async Task<(int year, string russianName)> GetRelasedAndName(string host, string cookie, string serieName)
        {
            string dir = System.IO.Path.GetDirectoryName($"Data/temp/lostfilm/{serieName}.relased");
            string pathRelased = $"Data/temp/lostfilm/{serieName}.relased";
            string pathName = $"Data/temp/lostfilm/{serieName}.name";
            if (System.IO.File.Exists(pathRelased))
            {
                if (int.TryParse(await System.IO.File.ReadAllTextAsync(pathRelased), out int cached))
                {
                    string russian = null;
                    if (System.IO.File.Exists(pathName))
                        russian = (await System.IO.File.ReadAllTextAsync(pathName)).Trim();
                    ParserLog.Write("lostfilm", $"    getRelased {serieName}: cache={cached}" + (string.IsNullOrEmpty(russian) ? "" : $" name={russian}"));
                    return (cached, russian);
                }
            }
            try
            {
                string html = await HttpClient.Get($"{host}/series/{serieName}", cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy);
                if (string.IsNullOrEmpty(html))
                {
                    ParserLog.Write("lostfilm", $"    getRelased {serieName}: empty page");
                    return (0, null);
                }
                var m = Regex.Match(html, @"itemprop=""dateCreated""\s+content=""(\d{4})-\d{2}-\d{2}""");
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out int year) || year <= 0)
                {
                    ParserLog.Write("lostfilm", $"    getRelased {serieName}: no dateCreated");
                    return (0, null);
                }
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
                System.IO.Directory.CreateDirectory(dir);
                await System.IO.File.WriteAllTextAsync(pathRelased, year.ToString());
                if (!string.IsNullOrWhiteSpace(russianName))
                    await System.IO.File.WriteAllTextAsync(pathName, russianName);
                ParserLog.Write("lostfilm", $"    getRelased {serieName}: fetched year={year}" + (string.IsNullOrEmpty(russianName) ? "" : $" name={russianName}"));
                return (year, russianName);
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"    getRelased {serieName}: error {ex.Message}");
                return (0, null);
            }
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

                // v_search.php возвращает 200 с HTML: meta refresh или location.replace на /V/?c=... (не HTTP 302), поэтому делаем второй запрос
                string searchHtml = await HttpClient.Get($"{host}/v_search.php?a={episodeId}", cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy);
                if (string.IsNullOrEmpty(searchHtml))
                {
                    ParserLog.Write("lostfilm", $"      GetMagnet: empty v_search response");
                    return default;
                }
                if (!searchHtml.Contains("inner-box--link"))
                {
                    string vPageUrl = null;
                    var mMeta = Regex.Match(searchHtml, @"(?:content=""[^""]*url\s*=\s*|location\.replace\s*\(\s*[""'])([^""]+)");
                    if (mMeta.Success)
                        vPageUrl = mMeta.Groups[1].Value.Trim();
                    if (string.IsNullOrEmpty(vPageUrl))
                        vPageUrl = Regex.Match(searchHtml, @"href=""(/V/\?[^""]+)""").Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(vPageUrl))
                    {
                        if (vPageUrl.StartsWith("/"))
                            vPageUrl = host.TrimEnd('/') + vPageUrl;
                        ParserLog.Write("lostfilm", $"      GetMagnet: fetch V page {vPageUrl.Substring(0, Math.Min(80, vPageUrl.Length))}...");
                        searchHtml = await HttpClient.Get(vPageUrl, cookie: cookie, referer: $"{host}/", useproxy: AppInit.conf.Lostfilm.useproxy) ?? "";
                    }
                    if (string.IsNullOrEmpty(searchHtml) || !searchHtml.Contains("inner-box--link"))
                    {
                        ParserLog.Write("lostfilm", $"      GetMagnet: no inner-box--link after V page");
                        return default;
                    }
                }
                string flat = Regex.Replace(searchHtml, @"[\n\r\t]+", " ");
                var linkRe = new Regex(@"<div\s+class=""inner-box--link\s+main""[^>]*><a\s+href=""([^""]+)""[^>]*>([^<]+)</a></div>", RegexOptions.IgnoreCase);
                foreach (Match m in linkRe.Matches(flat))
                {
                    string linkText = m.Groups[2].Value;
                    string quality = Regex.Match(linkText, @"(2160p|2060p|1440p|1080p|720p)", RegexOptions.IgnoreCase).Groups[1].Value;
                    if (string.IsNullOrEmpty(quality))
                        quality = Regex.Match(linkText, @"\b(1080|720)\b", RegexOptions.IgnoreCase).Groups[1].Value?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(quality))
                        continue;
                    quality = NormalizeQuality(quality);
                    string torrentUrl = m.Groups[1].Value;
                    if (string.IsNullOrEmpty(torrentUrl))
                        continue;
                    // ссылки с InSearch ведут на n.tracktor.site/td.php — без Referer: lostfilm не отдадут .torrent
                    byte[] data = await HttpClient.Download(torrentUrl, cookie: cookie, referer: $"{host}/");
                    if (data == null || data.Length == 0)
                    {
                        ParserLog.Write("lostfilm", $"      GetMagnet: tracktor empty response quality={quality}");
                        continue;
                    }
                    string magnet = BencodeTo.Magnet(data);
                    if (string.IsNullOrEmpty(magnet))
                    {
                        ParserLog.Write("lostfilm", $"      GetMagnet: BencodeTo.Magnet failed quality={quality}");
                        continue;
                    }
                    string sizeName = BencodeTo.SizeName(data);
                    return (magnet, quality, sizeName ?? "");
                }
                ParserLog.Write("lostfilm", $"      GetMagnet: no suitable quality link found");
            }
            catch (Exception ex)
            {
                ParserLog.Write("lostfilm", $"      GetMagnet error: {ex.Message}");
            }
            return default;
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
