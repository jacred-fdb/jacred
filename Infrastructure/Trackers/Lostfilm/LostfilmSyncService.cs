using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Lostfilm
{
    public class LostfilmSyncService
    {
        const string TrackerName = "lostfilm";

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        /// <summary>Парсит только первую страницу /new/ — актуальные новинки.</summary>
        public async Task<string> ParseAsync()
        {
            if (!EnsureConfig()) return "conf";

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    string host = AppInit.conf.Lostfilm.host ?? "https://www.lostfilm.tv";
                    string cookie = AppInit.conf.Lostfilm.cookie;

                    ParserLog.Write(TrackerName, "Parse (page /new/) start");
                    await ParsePage(host, cookie, 1, stopBeforeDate: null, startFromDate: null, preloadedHtml: null);
                    ParserLog.Write(TrackerName, $"Parse done in {sw.Elapsed.TotalSeconds:F1}s");
                    return "ok";
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Parse error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>Парсит страницы /new/ в указанном диапазоне. Без кэша, без фильтров по датам.</summary>
        public async Task<string> ParsePagesAsync(int pageFrom = 1, int pageTo = 1)
        {
            if (!EnsureConfig()) return "conf";

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    string host = AppInit.conf.Lostfilm.host ?? "https://www.lostfilm.tv";
                    string cookie = AppInit.conf.Lostfilm.cookie;
                    int delay = AppInit.conf.Lostfilm.parseDelay;

                    if (pageFrom < 1) pageFrom = 1;
                    if (pageTo < pageFrom) pageTo = pageFrom;

                    ParserLog.Write(TrackerName, $"ParsePages start pageFrom={pageFrom} pageTo={pageTo} host={host}");

                    string firstPageHtml = await HttpClient.Get($"{host}/new/", cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy, httpversion: 2);
                    int totalPages = LostfilmParser.ExtractTotalPagesFromNewPageHtml(firstPageHtml);
                    if (pageTo > totalPages)
                        pageTo = totalPages;
                    ParserLog.Write(TrackerName, $"Pagination: totalPages={totalPages} will parse pages {pageFrom}..{pageTo}");

                    for (int page = pageFrom; page <= pageTo; page++)
                    {
                        if (page > 1 && delay > 0)
                            await Task.Delay(delay);

                        await ParsePage(host, cookie, page, stopBeforeDate: null, startFromDate: null, page == 1 ? firstPageHtml : null);
                    }

                    ParserLog.Write(TrackerName, $"ParsePages done in {sw.Elapsed.TotalSeconds:F1}s");
                    return "ok";
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"ParsePages error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>Парсит страницу /series/{series}/seasons/ и добавляет торренты «полный сезон» (SD, 1080p, 720p) для каждого сезона с e=999.</summary>
        public async Task<string> ParseSeasonPacksAsync(string series)
        {
            if (!EnsureConfig()) return "conf";
            if (string.IsNullOrWhiteSpace(series))
                return "series required";

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    string host = AppInit.conf.Lostfilm.host ?? "https://www.lostfilm.tv";
                    string cookie = AppInit.conf.Lostfilm.cookie;
                    series = series.Trim();

                    ParserLog.Write(TrackerName, $"ParseSeasonPacks start series={series}");

                    string seasonsUrl = $"{host}/series/{series}/seasons/";
                    string html = await HttpClient.Get(seasonsUrl, cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy);
                    if (string.IsNullOrEmpty(html) || !html.Contains("LostFilm.TV"))
                    {
                        ParserLog.Write(TrackerName, $"ParseSeasonPacks: empty or invalid response {seasonsUrl}");
                        return "empty";
                    }

                    var (relased, russianName) = LostfilmParser.ParseRelasedAndNameFromHtml(html);
                    if (relased <= 0)
                    {
                        ParserLog.Write(TrackerName, $"ParseSeasonPacks: no relased in HTML for {series}");
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
                            ParserLog.Write(TrackerName, $"  no magnets: {series} s{seasonNum}");
                            continue;
                        }
                        DateTime createTime = DateTime.UtcNow;
                        foreach (var (magnet, quality, sizeName) in magnets)
                        {
                            string title = $"{name} / {originalname} / {seasonNum} сезон (полный сезон) [{relased}, {quality}]";
                            string url = vFullUrl + "#" + quality;
                            list.Add(new TorrentDetails
                            {
                                trackerName = TrackerName,
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
                        ParserLog.Write(TrackerName, $"  + {name} {seasonNum} сезон (полный): {magnets.Count} quality");
                    }

                    if (list.Count > 0)
                    {
                        await FileDB.AddOrUpdate(list, (t, db) => Task.FromResult(true));
                        ParserLog.Write(TrackerName, $"ParseSeasonPacks: added {list.Count} torrents");
                    }
                    else
                        ParserLog.Write(TrackerName, "ParseSeasonPacks: no season-pack links found");

                    ParserLog.Write(TrackerName, $"ParseSeasonPacks done in {sw.Elapsed.TotalSeconds:F1}s");
                    return "ok";
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"ParseSeasonPacks error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>Запрашивает /new/, парсит даты и возвращает, что мы извлекаем (dateStr, relased). Для проверки года в заголовках. Опционально ?series=slug фильтрует по сериалу (например Drops_of_God).</summary>
        public async Task<object> VerifyPageAsync(string series = null)
        {
            if (!EnsureConfig())
                return new { error = "conf" };

            string host = AppInit.conf.Lostfilm.host ?? "https://www.lostfilm.tv";
            string cookie = AppInit.conf.Lostfilm.cookie;
            string url = $"{host}/new/";

            string html = await HttpClient.Get(url, cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy, httpversion: 2);
            if (string.IsNullOrEmpty(html) || !html.Contains("LostFilm.TV"))
                return new { error = "empty", url };

            var items = LostfilmParser.ParseNewPageDates(html, host);
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

            return new
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
            };
        }

        /// <summary>Статистика по раздачам lostfilm в базе: количество, с магнитом, примеры ключей.</summary>
        public object GetStats()
        {
            if (!EnsureConfig())
                return new { error = "conf" };

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
                            if (t.trackerName != TrackerName)
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
            return new
            {
                total,
                withMagnet,
                withoutMagnet = total - withMagnet,
                keysCount = keysWithLostfilm.Count,
                keys = keysWithLostfilm.Take(50).ToArray(),
                keysMore = keysWithLostfilm.Count > 50 ? keysWithLostfilm.Count - 50 : 0
            };
        }

        static bool EnsureConfig() => AppInit.conf != null && AppInit.conf.Lostfilm != null;

        /// <param name="host">Базовый URL LostFilm (например https://www.lostfilm.tv)</param>
        /// <param name="cookie">Cookie для авторизованных запросов</param>
        /// <param name="page">Номер страницы ленты /new/ (1 = /new/, 2 = /new/page_2, ...)</param>
        /// <param name="stopBeforeDate">Если задано — прекратить парсинг, когда встретится раздача старше этой даты</param>
        /// <param name="startFromDate">Если задано — в обработку попадают только раздачи с createTime &lt;= startFromDate</param>
        /// <param name="preloadedHtml">Если не null, использовать вместо GET (для первой страницы при пагинации)</param>
        /// <returns>true если нужно прекратить парсинг (достигли stopBeforeDate)</returns>
        async Task<bool> ParsePage(string host, string cookie, int page, DateTime? stopBeforeDate, DateTime? startFromDate, string preloadedHtml = null)
        {
            string url = page > 1 ? $"{host}/new/page_{page}" : $"{host}/new/";
            string html = preloadedHtml;
            if (string.IsNullOrEmpty(html))
            {
                ParserLog.Write(TrackerName, $"Page {page}: GET {url}");
                html = await HttpClient.Get(url, cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy, httpversion: 2);
            }
            else
                ParserLog.Write(TrackerName, $"Page {page}: use preloaded");
            if (string.IsNullOrEmpty(html))
            {
                ParserLog.Write(TrackerName, $"Page {page}: empty response");
                return false;
            }
            if (!html.Contains("LostFilm.TV"))
            {
                ParserLog.Write(TrackerName, $"Page {page}: no 'LostFilm.TV' in response (cookies/redirect?)");
                return false;
            }

            string normalized = tParse.ReplaceBadNames(html);
            var list = new List<TorrentDetails>();

            // Карта urlPath -> (name, originalname) из hor-breaker: чтобы эпизоды из episode_links попадали в тот же бакет (Капли Бога : Drops of God), а не создавали дубликат (Drops of God : Drops of God).
            var horBreakerNameMap = LostfilmParser.BuildHorBreakerNameMap(normalized);

            await LostfilmParser.CollectFromEpisodeLinks(normalized, host, cookie, list, page, horBreakerNameMap);
            string source = "episode_links";
            if (list.Count == 0)
            {
                await LostfilmParser.CollectFromNewMovie(normalized, host, cookie, list, page);
                source = "new-movie";
            }
            if (list.Count == 0)
            {
                await LostfilmParser.CollectFromHorBreaker(normalized, host, cookie, list, page);
                source = "hor-breaker";
            }
            int beforeMovies = list.Count;
            await CollectFromMovies(normalized, host, cookie, list, page);

            // Один url — одна запись: убираем дубликаты по url, оставляем запись с русским названием (name != originalname), чтобы не было двух бакетов на один сериал.
            LostfilmParser.DedupeListByUrl(list);
            if (list.Count > beforeMovies)
                source = source + "+movies:" + (list.Count - beforeMovies);

            DateTime? oldestOnPage = list.Count > 0 ? list.Min(t => t.createTime) : (DateTime?)null;

            if (startFromDate.HasValue && list.Count > 0)
            {
                int before = list.Count;
                list = list.Where(t => t.createTime <= startFromDate.Value).ToList();
                if (list.Count < before)
                    ParserLog.Write(TrackerName, $"Page {page}: filtered by startFromDate {before} -> {list.Count}");
            }

            ParserLog.Write(TrackerName, $"Page {page}: collected {list.Count} items (source={source})");

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
                    // Сохраняем единое имя/originalname из кэша, чтобы бакет не разъезжался (Пони / Ponies, а не Ponies / Ponies).
                    if (!string.IsNullOrEmpty(cached.name))
                        t.name = cached.name;
                    if (!string.IsNullOrEmpty(cached.originalname))
                        t.originalname = cached.originalname;
                    return true;
                }

                var mag = t.types != null && t.types.Contains("movie")
                    ? await GetMagnetForMovie(host, cookie, t.url)
                    : await GetMagnet(host, cookie, t.url);
                if (string.IsNullOrEmpty(mag.magnet))
                {
                    noMagnet++;
                    ParserLog.Write(TrackerName, $"  no magnet: {t.url}");
                    return false;
                }

                t.magnet = mag.magnet;
                t.sizeName = mag.sizeName;
                if (!string.IsNullOrEmpty(mag.quality))
                {
                    string quality = LostfilmParser.NormalizeQuality(mag.quality);
                    if (t.title != null && t.title.TrimEnd().EndsWith("]"))
                        t.title = t.title.TrimEnd().Substring(0, t.title.TrimEnd().Length - 1) + ", " + quality + "]";
                    else
                        t.title = (t.title ?? "") + " [" + quality + "]";
                }
                added++;
                ParserLog.Write(TrackerName, $"  + {t.title?.Substring(0, Math.Min(60, t.title?.Length ?? 0))}... [{mag.quality}]");
                return true;
            });
            ParserLog.Write(TrackerName, $"Page {page}: added={added} fromCache={fromCache} noMagnet={noMagnet}");
            return false;
        }

        /// <summary>Собирает фильмы с /new/: ссылки на /movies/ и блоки с «Фильм» + дата. Для каждого получает V-страницу и добавляет раздачи по качествам (SD, 1080p, 720p).</summary>
        async Task CollectFromMovies(string html, string host, string cookie, List<TorrentDetails> list, int page)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    ParserLog.Write(TrackerName, $"  movie no V link: {name}");
                    continue;
                }

                var magnets = await GetMagnetsFromVPage(host, cookie, vPageUrl);
                if (magnets.Count == 0)
                {
                    ParserLog.Write(TrackerName, $"  movie no magnets: {name}");
                    continue;
                }

                string nameDec = HttpUtility.HtmlDecode(name);
                string originalnameDec = HttpUtility.HtmlDecode(originalname);
                foreach (var (magnet, quality, sizeName) in magnets)
                {
                    string q = LostfilmParser.NormalizeQuality(quality);
                    string title = $"{nameDec} / {originalnameDec} [Фильм, {relased}, {q}]";
                    list.Add(new TorrentDetails
                    {
                        trackerName = TrackerName,
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
                ParserLog.Write(TrackerName, $"  + movie {nameDec} ({magnets.Count} quality)");
            }
        }

        /// <summary>Со страницы фильма /movies/Slug извлекает ссылку на InSearch /V/?c=... (или редирект через v_search).</summary>
        async Task<string> GetVUrlFromMoviePage(string host, string cookie, string moviePageUrl)
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
                ParserLog.Write(TrackerName, $"  GetVUrlFromMoviePage: {ex.Message}");
                return null;
            }
        }

        /// <summary>Для фильма: получает V-URL со страницы фильма и возвращает первый доступный магнит (одно качество).</summary>
        async Task<(string magnet, string quality, string sizeName)> GetMagnetForMovie(string host, string cookie, string movieUrl)
        {
            string vPageUrl = await GetVUrlFromMoviePage(host, cookie, movieUrl);
            if (string.IsNullOrEmpty(vPageUrl))
                return default;
            var list = await GetMagnetsFromVPage(host, cookie, vPageUrl);
            if (list.Count > 0)
                return list[0];
            return default;
        }

        /// <summary>Парсит HTML страницы InSearch (V/?c=...) и возвращает все варианты качества с магнитами.</summary>
        async Task<List<(string magnet, string quality, string sizeName)>> ParseVPageQualityLinks(string host, string cookie, string searchHtml)
        {
            var linkUrls = LostfilmParser.ParseVPageQualityLinkUrls(searchHtml);
            var results = new List<(string magnet, string quality, string sizeName)>();

            foreach (var (torrentUrl, quality) in linkUrls)
            {
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
        async Task<string> FetchVPageHtml(string host, string cookie, string vPageUrlOrNull, string episodeIdForSearch = null)
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

        async Task<(string magnet, string quality, string sizeName)> GetMagnet(string host, string cookie, string episodeUrl)
        {
            try
            {
                string html = await HttpClient.Get(episodeUrl, cookie: cookie, useproxy: AppInit.conf.Lostfilm.useproxy);
                if (string.IsNullOrEmpty(html))
                {
                    ParserLog.Write(TrackerName, $"      GetMagnet: empty episode page {episodeUrl}");
                    return default;
                }
                var epMatch = Regex.Match(html, @"PlayEpisode\s*\(\s*['""]?(\d+)['""]?\s*\)");
                if (!epMatch.Success)
                {
                    ParserLog.Write(TrackerName, $"      GetMagnet: no PlayEpisode in {episodeUrl}");
                    return default;
                }
                string episodeId = epMatch.Groups[1].Value;
                ParserLog.Write(TrackerName, $"      GetMagnet: episodeId={episodeId}");

                string searchHtml = await FetchVPageHtml(host, cookie, null, episodeId);
                if (string.IsNullOrEmpty(searchHtml) || !searchHtml.Contains("inner-box--link"))
                {
                    ParserLog.Write(TrackerName, $"      GetMagnet: no inner-box--link after V page");
                    return default;
                }
                var list = await ParseVPageQualityLinks(host, cookie, searchHtml);
                if (list.Count > 0)
                    return list[0];
                ParserLog.Write(TrackerName, $"      GetMagnet: no suitable quality link found");
            }
            catch (Exception ex)
            {
                ParserLog.Write(TrackerName, $"      GetMagnet error: {ex.Message}");
            }
            return default;
        }

        /// <summary>По прямой ссылке на страницу V (например /V/?c=589&amp;s=4&amp;e=999) возвращает все качества (SD, 1080p, 720p) для полного сезона.</summary>
        async Task<List<(string magnet, string quality, string sizeName)>> GetMagnetsFromVPage(string host, string cookie, string vPageUrl)
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
                ParserLog.Write(TrackerName, $"      GetMagnetsFromVPage error: {ex.Message}");
                return new List<(string, string, string)>();
            }
        }
    }
}
