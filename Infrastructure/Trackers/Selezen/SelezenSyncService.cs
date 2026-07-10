using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Utils;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Infrastructure.Trackers.Selezen
{
    public class SelezenSyncService
    {
        const string TrackerName = "selezen";

        readonly IMemoryCache _memoryCache;

        static readonly SemaphoreSlim _loginSemaphore = new SemaphoreSlim(1, 1);

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        const string SelezenUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        public SelezenSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        /// <summary>Minimal headers for GET (list/detail). Curl works with minimal headers; Origin/Sec-Fetch-* can trigger WAF.</summary>
        static List<(string name, string val)> GetSelezenHeaders(string host)
        {
            return new List<(string, string)>
            {
                ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                ("Accept-Language", "en-US,en;q=0.9"),
            };
        }

        string Cookie()
        {
            if (_memoryCache.TryGetValue("selezen:cookie", out string cookie))
                return cookie;
            return null;
        }

        /// <summary>Попытка входа и кэширование cookie. Защита от параллельного вызова через SemaphoreSlim.</summary>
        async Task<bool> TakeLogin()
        {
            if (!await _loginSemaphore.WaitAsync(0))
            {
                ParserLog.Write(TrackerName, "TakeLogin skipped", new Dictionary<string, object> { { "reason", "login already in progress" } });
                return false;
            }

            try
            {
                string authKey = "selezen:TakeLogin()";
                if (_memoryCache.TryGetValue(authKey, out _))
                    return false;

                _memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(2));
                string host = AppInit.conf.Selezen.host?.TrimEnd('/') ?? "";

                if (string.IsNullOrWhiteSpace(AppInit.conf.Selezen.login?.u) || string.IsNullOrWhiteSpace(AppInit.conf.Selezen.login?.p))
                {
                    ParserLog.Write(TrackerName, "TakeLogin failed", new Dictionary<string, object> { { "reason", "credentials not configured" } });
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
                                string PHPSESSID = cook
                                    .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains("PHPSESSID="))
                                    .Select(line => new Regex("PHPSESSID=([^;]+)(;|$)").Match(line).Groups[1].Value)
                                    .LastOrDefault();
                                if (!string.IsNullOrWhiteSpace(PHPSESSID))
                                {
                                    _memoryCache.Set("selezen:cookie", $"PHPSESSID={PHPSESSID}; _ym_isad=2;", DateTime.Now.AddDays(1));
                                    ParserLog.Write(TrackerName, "TakeLogin success", new Dictionary<string, object> { { "host", host } });
                                    return true;
                                }
                            }
                            ParserLog.Write(TrackerName, "TakeLogin failed", new Dictionary<string, object> { { "reason", "no PHPSESSID in response" }, { "statusCode", (int)response.StatusCode } });
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ParserLog.Write(TrackerName, "TakeLogin error", new Dictionary<string, object> { { "message", ex.Message }, { "type", ex.GetType().Name } });
            }
            finally
            {
                _loginSemaphore.Release();
            }

            return false;
        }

        /// <summary>Парсинг страниц. parseFrom/parseTo через query: /cron/selezen/parse?parseFrom=1&amp;parseTo=5. Если оба 0 — парсится одна страница 1.</summary>
        public async Task<string> ParseAsync(int parseFrom = 0, int parseTo = 0)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = $"{AppInit.conf.Selezen.host}/relizy-ot-selezen/";
                    int startPage = parseFrom > 0 ? parseFrom : 1;
                    int endPage = parseTo > 0 ? parseTo : (parseFrom > 0 ? parseFrom : 1);
                    if (startPage > endPage) { int t = startPage; startPage = endPage; endPage = t; }

                    ParserLog.Write(TrackerName, "Starting parse", new Dictionary<string, object>
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
                            ParserLog.Write(TrackerName, "Parsing page", new Dictionary<string, object>
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

                    ParserLog.Write(TrackerName, "Parse completed successfully", new Dictionary<string, object>
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
                    ParserLog.Write(TrackerName, "Canceled", new Dictionary<string, object> { { "message", oce.Message } });
                    return "canceled";
                }
                catch (Exception ex)
                {
                    if (ex is OutOfMemoryException) throw;
                    ParserLog.Write(TrackerName, "Error", new Dictionary<string, object>
                    {
                        { "message", ex.Message },
                        { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                    });
                }

                return "ok";
            });
        }

        async Task<(int parsed, int added, int updated, int skipped, int failed)> parsePage(int page)
        {
            if (Cookie() == null && string.IsNullOrEmpty(AppInit.conf.Selezen.cookie))
            {
                if (await TakeLogin() == false)
                    return (0, 0, 0, 0, 0);
            }

            string cookie = AppInit.conf.Selezen.cookie ?? Cookie();
            string host = AppInit.conf.Selezen.host?.TrimEnd('/') ?? "";
            string listUrl = page <= 1 ? $"{host}/relizy-ot-selezen/" : $"{host}/relizy-ot-selezen/page/{page}/";

            var (html, listResponse) = await HttpClient.BaseGetAsync(listUrl, cookie: cookie, referer: host + "/", addHeaders: GetSelezenHeaders(host), timeoutSeconds: 15, useproxy: AppInit.conf.Selezen.useproxy);
            if (html == null || !html.Contains("dle_root"))
            {
                string reason = html == null
                    ? (listResponse != null ? $"HTTP {(int)listResponse.StatusCode} {listResponse.ReasonPhrase}" : "null response")
                    : "invalid content";
                ParserLog.Write(TrackerName, "Page parse failed", new Dictionary<string, object> { { "page", page }, { "url", listUrl }, { "reason", reason } });
                return (0, 0, 0, 0, 0);
            }
            if (!html.Contains($">{AppInit.conf.Selezen.login.u}<"))
            {
                if (string.IsNullOrEmpty(AppInit.conf.Selezen.cookie))
                    await TakeLogin();
                ParserLog.Write(TrackerName, "Page parse failed", new Dictionary<string, object> { { "page", page }, { "reason", "login not found in response" } });
                return (0, 0, 0, 0, 0);
            }

            var torrents = SelezenParser.ParseTorrentsFromListPage(html);

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
                            var match = db
                                .Where(kv => string.Equals(kv.Value.trackerName, TrackerName, StringComparison.OrdinalIgnoreCase))
                                .Select(kv => (kv, m: Regex.Match(kv.Key ?? "", @"/relizy-ot-selezen/(\d+)-")))
                                .FirstOrDefault(x => x.m.Success && x.m.Groups[1].Value == id);
                            if (match.kv.Key != null)
                            {
                                exists = true;
                                _tcache = match.kv.Value;
                            }
                        }
                    }

                    string fullnews = await HttpClient.Get(t.url, cookie: cookie, referer: host + "/", addHeaders: GetSelezenHeaders(host), timeoutSeconds: 15, useproxy: AppInit.conf.Selezen.useproxy);
                    if (fullnews != null)
                    {
                        string magnet = SelezenParser.ExtractMagnetFromDetailPage(fullnews);
                        if (!string.IsNullOrWhiteSpace(magnet))
                        {
                            t.magnet = magnet;
                            if (exists)
                            {
                                if (string.IsNullOrEmpty(_tcache?.magnet) || !string.Equals(_tcache.magnet, magnet, StringComparison.OrdinalIgnoreCase))
                                {
                                    updatedCount++;
                                    ParserLog.WriteUpdated(TrackerName, t, "magnet");
                                }
                                else
                                    skippedCount++;
                            }
                            else
                            {
                                addedCount++;
                                ParserLog.WriteAdded(TrackerName, t);
                            }
                            return true;
                        }
                    }
                    failedCount++;
                    ParserLog.WriteFailed(TrackerName, t, "no magnet");
                    return false;
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write(TrackerName, "Page completed", new Dictionary<string, object>
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
    }
}
