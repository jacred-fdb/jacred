using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using JacRed.Engine;
using Microsoft.Extensions.Caching.Memory;
using JacRed.Models.Details;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/selezen/[action]")]
    public class SelezenController : BaseController
    {
        #region Cookie / TakeLogin

        private static readonly SemaphoreSlim _loginSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>Minimal headers for GET (list/detail). Curl works with minimal headers; Origin/Sec-Fetch-* can trigger WAF.</summary>
        static List<(string name, string val)> GetSelezenHeaders(string host)
        {
            return new List<(string, string)>
            {
                ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                ("Accept-Language", "en-US,en;q=0.9"),
            };
        }

        const string SelezenUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        static string Cookie(IMemoryCache memoryCache)
        {
            if (memoryCache.TryGetValue("selezen:cookie", out string cookie))
                return cookie;
            return null;
        }

        /// <summary>Попытка входа и кэширование cookie. Защита от параллельного вызова через SemaphoreSlim.</summary>
        async Task<bool> TakeLogin()
        {
            if (!await _loginSemaphore.WaitAsync(0))
            {
                ParserLog.Write("selezen", "TakeLogin skipped", new Dictionary<string, object> { { "reason", "login already in progress" } });
                return false;
            }

            try
            {
                string authKey = "selezen:TakeLogin()";
                if (memoryCache.TryGetValue(authKey, out _))
                    return false;

                memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(2));
                string host = AppInit.conf.Selezen.host?.TrimEnd('/') ?? "";

                if (string.IsNullOrWhiteSpace(AppInit.conf.Selezen.login?.u) || string.IsNullOrWhiteSpace(AppInit.conf.Selezen.login?.p))
                {
                    ParserLog.Write("selezen", "TakeLogin failed", new Dictionary<string, object> { { "reason", "credentials not configured" } });
                    return false;
                }

                using (var clientHandler = new System.Net.Http.HttpClientHandler() { AllowAutoRedirect = false })
                {
                    clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                    using (var client = new System.Net.Http.HttpClient(clientHandler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(15);
                        client.MaxResponseContentBufferSize = 2000000;
                        client.DefaultRequestHeaders.Add("User-Agent", SelezenUserAgent);
                        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                        client.DefaultRequestHeaders.Add("Referer", host + "/");
                        client.DefaultRequestHeaders.Add("Origin", host);

                        var postParams = new Dictionary<string, string>
                        {
                            { "login_name", AppInit.conf.Selezen.login.u },
                            { "login_password", AppInit.conf.Selezen.login.p },
                            { "login_not_save", "1" },
                            { "login", "submit" }
                        };

                        using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                        using (var response = await client.PostAsync(host, postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string PHPSESSID = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line)) continue;
                                    if (line.Contains("PHPSESSID="))
                                        PHPSESSID = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }
                                if (!string.IsNullOrWhiteSpace(PHPSESSID))
                                {
                                    memoryCache.Set("selezen:cookie", $"PHPSESSID={PHPSESSID}; _ym_isad=2;", DateTime.Now.AddDays(1));
                                    ParserLog.Write("selezen", "TakeLogin success", new Dictionary<string, object> { { "host", host } });
                                    return true;
                                }
                            }
                            ParserLog.Write("selezen", "TakeLogin failed", new Dictionary<string, object> { { "reason", "no PHPSESSID in response" }, { "statusCode", (int)response.StatusCode } });
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ParserLog.Write("selezen", "TakeLogin error", new Dictionary<string, object> { { "message", ex.Message }, { "type", ex.GetType().Name } });
            }
            finally
            {
                _loginSemaphore.Release();
            }

            return false;
        }
        #endregion

        #region Parse

        static volatile bool _workParse = false;
        static readonly object _workParseLock = new object();

        static bool TryStartParse()
        {
            lock (_workParseLock)
            {
                if (_workParse) return false;
                _workParse = true;
                return true;
            }
        }

        static void EndParse()
        {
            lock (_workParseLock) { _workParse = false; }
        }

        /// <summary>Парсинг страниц. parseFrom/parseTo через query: /cron/selezen/parse?parseFrom=1&amp;parseTo=5. Если оба 0 — парсится одна страница 1.</summary>
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            if (AppInit.conf?.disable_trackers != null && AppInit.conf.disable_trackers.Contains("selezen", StringComparer.OrdinalIgnoreCase))
                return "disabled";
            if (!TryStartParse())
                return "work";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = $"{AppInit.conf.Selezen.host}/relizy-ot-selezen/";
                int startPage = parseFrom > 0 ? parseFrom : 1;
                int endPage = parseTo > 0 ? parseTo : (parseFrom > 0 ? parseFrom : 1);
                if (startPage > endPage) { int t = startPage; startPage = endPage; endPage = t; }

                ParserLog.Write("selezen", "Starting parse", new Dictionary<string, object>
                {
                    { "parseFrom", parseFrom },
                    { "parseTo", parseTo },
                    { "startPage", startPage },
                    { "endPage", endPage },
                    { "baseUrl", baseUrl }
                });

                int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                for (int page = startPage; page <= endPage; page++)
                {
                    if (page > startPage)
                        await Task.Delay(AppInit.conf.Selezen.parseDelay);

                    if (page > 1)
                    {
                        ParserLog.Write("selezen", "Parsing page", new Dictionary<string, object>
                        {
                            { "page", page },
                            { "url", page <= 1 ? baseUrl : $"{AppInit.conf.Selezen.host}/relizy-ot-selezen/page/{page}/" }
                        });
                    }

                    var (parsed, added, updated, skipped, failed) = await parsePage(page);
                    totalParsed += parsed;
                    totalAdded += added;
                    totalUpdated += updated;
                    totalSkipped += skipped;
                    totalFailed += failed;
                }

                ParserLog.Write("selezen", "Parse completed successfully", new Dictionary<string, object>
                {
                    { "tookSec", sw.Elapsed.TotalSeconds },
                    { "parsed", totalParsed },
                    { "added", totalAdded },
                    { "updated", totalUpdated },
                    { "skipped", totalSkipped },
                    { "failed", totalFailed }
                });
            }
            catch (OperationCanceledException oce)
            {
                ParserLog.Write("selezen", "Canceled", new Dictionary<string, object> { { "message", oce.Message } });
                return "canceled";
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
                ParserLog.Write("selezen", "Error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
            }
            finally
            {
                EndParse();
            }

            return "ok";
        }
        #endregion

        #region parsePage

        async Task<(int parsed, int added, int updated, int skipped, int failed)> parsePage(int page)
        {
            if (Cookie(memoryCache) == null && string.IsNullOrEmpty(AppInit.conf.Selezen.cookie))
            {
                if (await TakeLogin() == false)
                    return (0, 0, 0, 0, 0);
            }

            string cookie = AppInit.conf.Selezen.cookie ?? Cookie(memoryCache);
            string host = AppInit.conf.Selezen.host?.TrimEnd('/') ?? "";
            string listUrl = page <= 1 ? $"{host}/relizy-ot-selezen/" : $"{host}/relizy-ot-selezen/page/{page}/";

            var (html, listResponse) = await HttpClient.BaseGetAsync(listUrl, cookie: cookie, referer: host + "/", addHeaders: GetSelezenHeaders(host), timeoutSeconds: 15, useproxy: AppInit.conf.Selezen.useproxy);
            if (html == null || !html.Contains("dle_root"))
            {
                string reason = html == null
                    ? (listResponse != null ? $"HTTP {(int)listResponse.StatusCode} {listResponse.ReasonPhrase}" : "null response")
                    : "invalid content";
                ParserLog.Write("selezen", "Page parse failed", new Dictionary<string, object> { { "page", page }, { "url", listUrl }, { "reason", reason } });
                return (0, 0, 0, 0, 0);
            }
            if (!html.Contains($">{AppInit.conf.Selezen.login.u}<"))
            {
                if (string.IsNullOrEmpty(AppInit.conf.Selezen.cookie))
                    await TakeLogin();
                ParserLog.Write("selezen", "Page parse failed", new Dictionary<string, object> { { "page", page }, { "reason", "login not found in response" } });
                return (0, 0, 0, 0, 0);
            }

            var torrents = new List<TorrentBaseDetails>();
            foreach (string row in tParse.ReplaceBadNames(html).Split("card overflow-hidden").Skip(1))
            {
                if (row.Contains(">Аниме</a>"))
                    continue;

                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, @"\s+", " ");
                    return res.Trim();
                }

                if (string.IsNullOrWhiteSpace(row)) continue;

                DateTime createTime = tParse.ParseCreateTime(Match(@"class=""bx bx-calendar""></span>\s*([0-9]{2}\.[0-9]{2}\.[0-9]{4} [0-9]{2}:[0-9]{2})</a>"), "dd.MM.yyyy HH:mm");
                if (createTime == default) continue;

                var g = Regex.Match(row, @"<a href=""(https?://[^""]+)""><h4 class=""card-title"">([^<]+)</h4>").Groups;
                string url = g[1].Value;
                string title = g[2].Value;
                if (string.IsNullOrWhiteSpace(url) || !url.Contains(".html", StringComparison.OrdinalIgnoreCase))
                    continue;

                string _sid = Match(@"<i class=""bx bx-chevrons-up""></i>([0-9 ]+)").Trim();
                string _pir = Match(@"<i class=""bx bx-chevrons-down""></i>([0-9 ]+)").Trim();
                string sizeName = Match(@"<span class=""bx bx-download""></span>([^<]+)</a>").Trim();
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                int relased = 0;
                string name = null, originalname = null;
                g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;
                    if (int.TryParse(g[3].Value, out int _yer)) relased = _yer;
                }
                else
                {
                    g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;
                    originalname = g[2].Value;
                    if (int.TryParse(g[3].Value, out int _yer)) relased = _yer;
                }
                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Тип: мультфильм по жанру в карточке; сериал по [S01]/[01x01-02 из 09] или TVShows в title/url; иначе movie
                string[] types = new string[] { "movie" };
                if (row.Contains(">Мульт") || row.Contains(">мульт"))
                    types = new string[] { "multfilm" };
                else if (title.IndexOf("TVShows", StringComparison.OrdinalIgnoreCase) >= 0
                    || Regex.IsMatch(title, @"\[S\d+\]")
                    || Regex.IsMatch(title, @"\[\d+[xх]\d+")  // 01x01 или 01х01 (латинская/кириллическая х)
                    || (url.IndexOf("tvshows", StringComparison.OrdinalIgnoreCase) >= 0))
                    types = new string[] { "serial" };
                int.TryParse(_sid, out int sid);
                int.TryParse(_pir, out int pir);

                torrents.Add(new TorrentDetails()
                {
                    trackerName = "selezen",
                    types = types,
                    url = url,
                    title = title,
                    sid = sid,
                    pir = pir,
                    sizeName = sizeName,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    relased = relased
                });
            }

            int parsedCount = torrents.Count;
            int addedCount = 0, updatedCount = 0, skippedCount = 0, failedCount = 0;

            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, async (t, db) =>
                {
                    bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);
                    if (!exists)
                    {
                        var idMatch = Regex.Match(t.url ?? "", @"/relizy-ot-selezen/(\d+)-");
                        if (idMatch.Success)
                        {
                            string id = idMatch.Groups[1].Value;
                            foreach (var kv in db)
                            {
                                if (!string.Equals(kv.Value.trackerName, "selezen", StringComparison.OrdinalIgnoreCase)) continue;
                                var m = Regex.Match(kv.Key ?? "", @"/relizy-ot-selezen/(\d+)-");
                                if (m.Success && m.Groups[1].Value == id) { exists = true; _tcache = kv.Value; break; }
                            }
                        }
                    }

                    string fullnews = await HttpClient.Get(t.url, cookie: cookie, referer: host + "/", addHeaders: GetSelezenHeaders(host), timeoutSeconds: 15, useproxy: AppInit.conf.Selezen.useproxy);
                    if (fullnews != null)
                    {
                        string magnet = Regex.Match(fullnews, "href=\"(magnet:\\?xt=urn:btih:[^\"]+)\"").Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(magnet))
                        {
                            t.magnet = magnet;
                            if (exists)
                            {
                                if (string.IsNullOrEmpty(_tcache?.magnet) || !string.Equals(_tcache.magnet, magnet, StringComparison.OrdinalIgnoreCase))
                                {
                                    updatedCount++;
                                    ParserLog.WriteUpdated("selezen", t, "magnet");
                                }
                                else
                                    skippedCount++;
                            }
                            else
                            {
                                addedCount++;
                                ParserLog.WriteAdded("selezen", t);
                            }
                            return true;
                        }
                    }
                    failedCount++;
                    ParserLog.WriteFailed("selezen", t, "no magnet");
                    return false;
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write("selezen", "Page completed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "parsed", parsedCount },
                    { "added", addedCount },
                    { "updated", updatedCount },
                    { "skipped", skippedCount },
                    { "failed", failedCount }
                });
            }

            return (parsedCount, addedCount, updatedCount, skippedCount, failedCount);
        }
        #endregion
    }
}
