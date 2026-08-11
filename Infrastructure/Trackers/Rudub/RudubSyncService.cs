using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Infrastructure.Trackers.Rudub
{
    /// <summary>
    /// RuDub sync — login/cookie required; browse HD 1080 + HD 2160 only (videoformat 4/5).
    /// Torrent downloads use MagnetNoTrackers so session passkeys never enter FDB.
    /// </summary>
    public class RudubSyncService
    {
        const string TrackerName = RudubParser.TrackerName;
        const string CookiePhpSessId = "PHPSESSID";
        const string CookiePass = "pass";
        const string CookieUid = "uid";
        const string EndpointLogin = "/takelogin.php";
        const string EndpointBrowse = "/browse.php";
        const string CacheCookie = "rudub:cookie";
        const string ParamUsername = "username";
        const string ParamPassword = "password";

        static readonly TimeSpan CookieCacheDuration = TimeSpan.FromDays(1);
        static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1, 1);
        static readonly Regex RegexCookieValue = new Regex("([^;]+)(;|$)", RegexOptions.Compiled);
        static readonly TrackerParseLock ParseLock = new TrackerParseLock();
        static readonly Encoding Cp1251 = Encoding.GetEncoding(1251);

        readonly IMemoryCache _memoryCache;

        public RudubSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        static string Host() => (AppInit.conf.Rudub?.rqHost() ?? "").TrimEnd('/');

        string Cookie()
        {
            if (!string.IsNullOrWhiteSpace(AppInit.conf.Rudub?.cookie))
                return AppInit.conf.Rudub.cookie.Trim();

            if (_memoryCache.TryGetValue(CacheCookie, out string cookie))
                return cookie;

            return null;
        }

        async Task<bool> CheckLoginAsync()
        {
            if (Cookie() != null)
                return true;

            if (!string.IsNullOrWhiteSpace(AppInit.conf.Rudub?.login?.u) &&
                !string.IsNullOrWhiteSpace(AppInit.conf.Rudub?.login?.p))
            {
                return await TakeLoginAsync();
            }

            ParserLog.Write(TrackerName, "No cookie or login credentials available");
            return false;
        }

        async Task<bool> TakeLoginAsync()
        {
            if (!await LoginSemaphore.WaitAsync(TimeSpan.FromSeconds(15)))
            {
                ParserLog.Write(TrackerName, "TakeLogin skipped: login semaphore timeout (15s)");
                return false;
            }

            try
            {
                if (Cookie() != null)
                    return true;

                string login = AppInit.conf.Rudub.login.u;
                string pass = AppInit.conf.Rudub.login.p;
                string host = Host();
                if (string.IsNullOrEmpty(host))
                    return false;

                var clientHandler = new System.Net.Http.HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                };

                using var client = new System.Net.Http.HttpClient(clientHandler)
                {
                    Timeout = TimeSpan.FromSeconds(15),
                    MaxResponseContentBufferSize = 2_000_000
                };
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "user-agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var postParams = new Dictionary<string, string>
                {
                    { ParamUsername, login },
                    { ParamPassword, pass }
                };

                using var postContent = new System.Net.Http.FormUrlEncodedContent(postParams);
                using var response = await client.PostAsync($"{host}{EndpointLogin}", postContent);
                if (!response.Headers.TryGetValues("Set-Cookie", out var cook))
                {
                    ParserLog.Write(TrackerName, "Login FAILED — no Set-Cookie");
                    return false;
                }

                string sessid = ExtractCookieValue(cook, CookiePhpSessId);
                string passCookie = ExtractCookieValue(cook, CookiePass);
                string uid = ExtractCookieValue(cook, CookieUid);

                if (string.IsNullOrWhiteSpace(sessid) || string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(passCookie))
                {
                    ParserLog.Write(TrackerName, "Login FAILED — missing PHPSESSID/uid/pass");
                    return false;
                }

                string cookieStr = $"{CookiePhpSessId}={sessid}; {CookieUid}={uid}; {CookiePass}={passCookie}";
                _memoryCache.Set(CacheCookie, cookieStr, CookieCacheDuration);
                ParserLog.Write(TrackerName, "Login OK");
                return true;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                ParserLog.Write(TrackerName, $"Login HTTP error: {ex.Message}");
            }
            catch (OperationCanceledException ex)
            {
                ParserLog.Write(TrackerName, $"Login cancelled: {ex.Message}");
            }
            catch (System.IO.IOException ex)
            {
                ParserLog.Write(TrackerName, $"Login error: {ex.GetType().Name}: {ex.Message}");
            }
            catch (SocketException ex)
            {
                ParserLog.Write(TrackerName, $"Login error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                LoginSemaphore.Release();
            }

            return false;
        }

        static string ExtractCookieValue(IEnumerable<string> cookieHeaders, string cookieName)
        {
            string cookieKey = $"{cookieName}=";
            string candidate = (cookieHeaders ?? Enumerable.Empty<string>())
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && line.Contains(cookieKey, StringComparison.Ordinal));

            if (candidate == null)
                return null;

            var match = RegexCookieValue.Match(candidate.Substring(candidate.IndexOf(cookieKey, StringComparison.Ordinal) + cookieKey.Length));
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Safety cap for <c>limit_page</c> (each page × 2 videoformats + torrent downloads).</summary>
        public const int MaxLimitPages = 100;

        /// <param name="parseFrom">Inclusive start page (0-based).</param>
        /// <param name="parseTo">Inclusive end page.</param>
        /// <param name="limit_page">If &gt; 0 and both parseFrom/parseTo are 0, parse pages 0..(N-1).</param>
        public async Task<string> ParseAsync(int parseFrom = 0, int parseTo = 0, int limit_page = 0)
        {
            if (string.IsNullOrEmpty(Host()))
                return TrackerSyncHelpers.DisabledResult;

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, ParseLock, checkDisabled: false, async () =>
            {
                if (!await CheckLoginAsync())
                    return "login error";

                try
                {
                    var sw = Stopwatch.StartNew();
                    ResolvePageRange(parseFrom, parseTo, limit_page, out int startPage, out int endPage);

                    ParserLog.Write(TrackerName, "Starting parse", new Dictionary<string, object>
                    {
                        { "parseFrom", parseFrom },
                        { "parseTo", parseTo },
                        { "limit_page", limit_page },
                        { "startPage", startPage },
                        { "endPage", endPage },
                        { "pages", endPage - startPage + 1 },
                        { "videoformats", string.Join(",", RudubParser.PreferredVideoFormats) },
                        { "host", Host() }
                    });

                    int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;
                    bool firstRequest = true;

                    foreach (int videoFormat in RudubParser.PreferredVideoFormats)
                    {
                        for (int page = startPage; page <= endPage; page++)
                        {
                            if (!firstRequest && AppInit.conf.Rudub.parseDelay > 0)
                                await Task.Delay(AppInit.conf.Rudub.parseDelay);
                            firstRequest = false;

                            ParserLog.Write(TrackerName, $"Page {page} videoformat={videoFormat}");
                            var result = await ParsePageAsync(page, videoFormat);
                            totalParsed += result.parsed;
                            totalAdded += result.added;
                            totalUpdated += result.updated;
                            totalSkipped += result.skipped;
                            totalFailed += result.failed;
                        }
                    }

                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                        new Dictionary<string, object>
                        {
                            { "parsed", totalParsed },
                            { "added", totalAdded },
                            { "updated", totalUpdated },
                            { "skipped", totalSkipped },
                            { "failed", totalFailed }
                        });
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                }

                return "ok";
            });
        }

        /// <summary>
        /// Resolves browse page range. Prefer explicit parseFrom/parseTo when either is non-zero;
        /// otherwise <paramref name="limit_page"/> means the first N pages (0..N-1).
        /// </summary>
        internal static void ResolvePageRange(int parseFrom, int parseTo, int limit_page, out int startPage, out int endPage)
        {
            if (limit_page > 0 && parseFrom == 0 && parseTo == 0)
            {
                int n = Math.Clamp(limit_page, 1, MaxLimitPages);
                startPage = 0;
                endPage = n - 1;
                return;
            }

            startPage = parseFrom >= 0 ? parseFrom : 0;
            endPage = parseTo >= 0 ? parseTo : startPage;
            if (startPage > endPage)
                (startPage, endPage) = (endPage, startPage);
        }

        async Task<(int parsed, int added, int updated, int skipped, int failed)> ParsePageAsync(int page, int videoFormat)
        {
            string host = Host();
            string url =
                $"{host}{EndpointBrowse}?incldead=0&sort=4&type=desc&videoformat={videoFormat}&page={page}";

            string html = await HttpClient.Get(url, encoding: Cp1251, cookie: Cookie(), useproxy: AppInit.conf.Rudub.useproxy);
            if (html == null || !html.Contains(RudubParser.ValidationMarker, StringComparison.Ordinal))
            {
                ParserLog.Write(TrackerName, "Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "videoformat", videoFormat },
                    { "reason", html == null ? "null response" : "invalid content" }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = RudubParser.ParseTorrentListFromHtml(html, host);
            int parsedCount = torrents.Count;
            int addedCount = 0, updatedCount = 0, skippedCount = 0, failedCount = 0;

            if (torrents.Count > 0)
            {
                string referer = $"{host}{EndpointBrowse}";
                await FileDB.AddOrUpdate(torrents, async (t, db) =>
                {
                    try
                    {
                        string cookie = Cookie();
                        bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);

                        if (exists && string.Equals(_tcache.title?.Trim(), t.title?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            bool typesChanged = !RudubParser.TypesEqual(t.types, _tcache.types);
                            if (typesChanged)
                            {
                                updatedCount++;
                                ParserLog.WriteUpdated(TrackerName, t,
                                    $"types updated: [{string.Join(", ", _tcache.types ?? Array.Empty<string>())}] -> [{string.Join(", ", t.types ?? Array.Empty<string>())}]");
                                return true;
                            }

                            var extractResult = await DownloadAndExtractTorrentAsync(t.downloadUri, cookie, referer);
                            if (extractResult.error != null)
                            {
                                skippedCount++;
                                ParserLog.WriteSkipped(TrackerName, _tcache, extractResult.error);
                                return false;
                            }

                            bool magnetChanged = !string.Equals(_tcache.magnet?.Trim(), extractResult.magnet.Trim(), StringComparison.OrdinalIgnoreCase);
                            bool sizeChanged = !string.Equals(_tcache.sizeName?.Trim() ?? "", extractResult.sizeName.Trim(), StringComparison.OrdinalIgnoreCase);
                            if (!magnetChanged && !sizeChanged)
                            {
                                skippedCount++;
                                ParserLog.WriteSkipped(TrackerName, _tcache, "no changes");
                                return false;
                            }

                            t.magnet = extractResult.magnet;
                            t.sizeName = extractResult.sizeName;
                            updatedCount++;
                            ParserLog.WriteUpdated(TrackerName, t,
                                magnetChanged && sizeChanged ? "magnet and size updated" : (magnetChanged ? "magnet updated" : "size updated"));
                            return true;
                        }

                        var result = await DownloadAndExtractTorrentAsync(t.downloadUri, cookie, referer);
                        if (result.error != null)
                        {
                            failedCount++;
                            ParserLog.WriteFailed(TrackerName, t, result.error);
                            return false;
                        }

                        t.magnet = result.magnet;
                        t.sizeName = result.sizeName;

                        if (exists)
                        {
                            updatedCount++;
                            ParserLog.WriteUpdated(TrackerName, t, "title changed or new data");
                        }
                        else
                        {
                            addedCount++;
                            ParserLog.WriteAdded(TrackerName, t);
                        }

                        return true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (SystemException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        ParserLog.WriteFailed(TrackerName, t, $"exception: {ex.GetType().Name}: {ex.Message}");
                        return false;
                    }
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write(TrackerName, $"Page {page} vf={videoFormat} completed",
                    new Dictionary<string, object>
                    {
                        { "parsed", parsedCount },
                        { "added", addedCount },
                        { "updated", updatedCount },
                        { "skipped", skippedCount },
                        { "failed", failedCount }
                    });
            }

            return (parsedCount, addedCount, updatedCount, skippedCount, failedCount);
        }

        async Task<(byte[] data, string magnet, string sizeName, string error)>
            DownloadAndExtractTorrentAsync(string downloadUri, string cookie, string referer)
        {
            byte[] torrentData = await HttpClient.Download(
                downloadUri,
                cookie: cookie,
                referer: referer,
                useproxy: AppInit.conf.Rudub.useproxy);

            if (torrentData == null || torrentData.Length == 0)
            {
                string cookieStatus = string.IsNullOrWhiteSpace(cookie) ? "no cookie" : "cookie present";
                return (null, null, null, $"failed to download torrent (null or empty), downloadUri={downloadUri}, {cookieStatus}");
            }

            if (!RudubParser.IsValidBencodedTorrent(torrentData))
                return (torrentData, null, null, $"downloaded HTML instead of torrent file, downloadUri={downloadUri}");

            // Strip announce/passkey from magnet — authenticated .torrent must not leak into FDB.
            string magnet = BencodeTo.MagnetNoTrackers(torrentData);
            string sizeName = BencodeTo.SizeName(torrentData);

            if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
                return (torrentData, magnet, sizeName, null);

            string errorDetails =
                $"magnet={(string.IsNullOrWhiteSpace(magnet) ? "null" : "ok")}, sizeName={(string.IsNullOrWhiteSpace(sizeName) ? "null" : "ok")}, torrentSize={torrentData.Length}";
            return (torrentData, null, null, $"failed to extract magnet or size: {errorDetails}");
        }
    }
}
