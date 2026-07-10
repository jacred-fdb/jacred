using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Utils;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Infrastructure.Trackers.Baibako
{
    public class BaibakoSyncService
    {
        const string TrackerName = "baibako";
        const string CookiePhpSessId = "PHPSESSID";
        const string CookiePass = "pass";
        const string CookieUid = "uid";
        const string EndpointLogin = "/takelogin.php";
        const string EndpointBrowse = "/browse.php";
        const string CacheCookie = "baibako:cookie";
        const string ParamUsername = "username";
        const string ParamPassword = "password";

        static readonly TimeSpan CookieCacheDuration = TimeSpan.FromDays(1);
        static readonly SemaphoreSlim loginSemaphore = new SemaphoreSlim(1, 1);
        static readonly Regex RegexCookieValue = new Regex("([^;]+)(;|$)", RegexOptions.Compiled);

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        readonly IMemoryCache _memoryCache;

        public BaibakoSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        string Cookie()
        {
            if (!string.IsNullOrWhiteSpace(AppInit.conf.Baibako.cookie))
                return AppInit.conf.Baibako.cookie;

            if (_memoryCache.TryGetValue(CacheCookie, out string cookie))
                return cookie;

            return null;
        }

        async Task<bool> CheckLogin()
        {
            if (Cookie() != null)
                return true;

            if (!string.IsNullOrWhiteSpace(AppInit.conf.Baibako.login?.u) &&
                !string.IsNullOrWhiteSpace(AppInit.conf.Baibako.login?.p))
            {
                return await TakeLogin();
            }

            ParserLog.Write(TrackerName, "No cookie or login credentials available");
            return false;
        }

        async Task<bool> TakeLogin()
        {
            await loginSemaphore.WaitAsync();
            try
            {
                if (Cookie() != null)
                    return true;

                var login = AppInit.conf.Baibako.login.u;
                var pass = AppInit.conf.Baibako.login.p;
                var host = AppInit.conf.Baibako.host;
                if (string.IsNullOrEmpty(host)) return false;

                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                };

                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.MaxResponseContentBufferSize = 2000000;
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                    var postParams = new Dictionary<string, string>
                    {
                        { ParamUsername, login },
                        { ParamPassword, pass }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{host}{EndpointLogin}", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string sessid = ExtractCookieValue(cook, CookiePhpSessId);
                                string passCookie = ExtractCookieValue(cook, CookiePass);
                                string uid = ExtractCookieValue(cook, CookieUid);

                                if (!string.IsNullOrWhiteSpace(sessid) && !string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(passCookie))
                                {
                                    string cookieStr = $"{CookiePhpSessId}={sessid}; {CookieUid}={uid}; {CookiePass}={passCookie}";
                                    _memoryCache.Set(CacheCookie, cookieStr, CookieCacheDuration);
                                    ParserLog.Write(TrackerName, "Login OK");
                                    return true;
                                }
                            }
                        }
                    }
                }
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
                ParserLog.Write(TrackerName, $"Login error: {ex.GetType().Name}: {ex}");
            }
            catch (SocketException ex)
            {
                ParserLog.Write(TrackerName, $"Login error: {ex.GetType().Name}: {ex}");
            }
            finally
            {
                loginSemaphore.Release();
            }

            return false;
        }

        string ExtractCookieValue(IEnumerable<string> cookieHeaders, string cookieName)
        {
            string cookieKey = $"{cookieName}=";
            string candidate = (cookieHeaders ?? Enumerable.Empty<string>())
                .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains(cookieKey))
                .FirstOrDefault();

            if (candidate == null) return null;

            var match = RegexCookieValue.Match(candidate.Substring(candidate.IndexOf(cookieKey) + cookieKey.Length));
            return match.Success ? match.Groups[1].Value : null;
        }

        public async Task<string> ParseAsync(int parseFrom = 0, int parseTo = 0)
        {
            if (string.IsNullOrEmpty(AppInit.conf.Baibako.host))
                return TrackerSyncHelpers.DisabledResult;

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                if (!await CheckLogin())
                    return "login error";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = $"{AppInit.conf.Baibako.host}{EndpointBrowse}";

                    int startPage = parseFrom >= 0 ? parseFrom : 0;
                    int endPage = parseTo >= 0 ? parseTo : (parseFrom >= 0 ? parseFrom : 0);

                    if (startPage > endPage)
                    {
                        int temp = startPage;
                        startPage = endPage;
                        endPage = temp;
                    }

                    ParserLog.Write(TrackerName, $"Starting parse", new Dictionary<string, object>
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
                            await Task.Delay(AppInit.conf.Baibako.parseDelay);

                        ParserLog.Write(TrackerName, $"Page {page}: {baseUrl}?page={page}");
                        var result = await parsePage(page);
                        totalParsed += result.parsed;
                        totalAdded += result.added;
                        totalUpdated += result.updated;
                        totalSkipped += result.skipped;
                        totalFailed += result.failed;
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

        async Task<(int parsed, int added, int updated, int skipped, int failed)> parsePage(int page)
        {
            string html = await HttpClient.Get($"{AppInit.conf.Baibako.host}{EndpointBrowse}?page={page}", encoding: Encoding.GetEncoding(1251), cookie: Cookie());
            if (html == null || !html.Contains(BaibakoParser.ValidationNavTop))
            {
                ParserLog.Write(TrackerName, $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "reason", html == null ? "null response" : "invalid content" }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = BaibakoParser.ParseTorrentListFromHtml(html, AppInit.conf.Baibako.host, page);

            int parsedCount = torrents.Count;
            int addedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, async (t, db) =>
                {
                    try
                    {
                        string cookie = Cookie();
                        string referer = $"{AppInit.conf.Baibako.host}{EndpointBrowse}";

                        bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);

                        if (exists && string.Equals(_tcache.title?.Trim(), t.title?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            bool typesChanged = !BaibakoParser.TypesEqual(t.types, _tcache.types);

                            if (typesChanged)
                            {
                                updatedCount++;
                                ParserLog.WriteUpdated(TrackerName, t, $"types updated: [{string.Join(", ", _tcache.types ?? new string[0])}] -> [{string.Join(", ", t.types ?? new string[0])}]");
                                return true;
                            }

                            var extractResult = await DownloadAndExtractTorrent(t.downloadUri, cookie, referer);
                            if (extractResult.error != null)
                            {
                                skippedCount++;
                                ParserLog.WriteSkipped(TrackerName, _tcache, extractResult.error);
                                return false;
                            }

                            string magnetCompare = _tcache.magnet?.Trim() ?? "";
                            string sizeCompare = _tcache.sizeName?.Trim() ?? "";
                            string newMagnetCompare = extractResult.magnet.Trim();
                            string newSizeCompare = extractResult.sizeName.Trim();

                            bool magnetChanged = !string.Equals(magnetCompare, newMagnetCompare, StringComparison.OrdinalIgnoreCase);
                            bool sizeChanged = !string.Equals(sizeCompare, newSizeCompare, StringComparison.OrdinalIgnoreCase);

                            if (!magnetChanged && !sizeChanged)
                            {
                                skippedCount++;
                                ParserLog.WriteSkipped(TrackerName, _tcache, "no changes");
                                return false;
                            }

                            t.magnet = extractResult.magnet;
                            t.sizeName = extractResult.sizeName;
                            updatedCount++;
                            string reason = magnetChanged && sizeChanged ? "magnet and size updated" : (magnetChanged ? "magnet updated" : "size updated");
                            ParserLog.WriteUpdated(TrackerName, t, reason);
                            return true;
                        }

                        var result = await DownloadAndExtractTorrent(t.downloadUri, cookie, referer);
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
                ParserLog.Write(TrackerName, $"Page {page} completed",
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
            DownloadAndExtractTorrent(string downloadUri, string cookie, string referer)
        {
            byte[] torrentData = await HttpClient.Download(downloadUri, cookie: cookie, referer: referer);

            if (torrentData == null || torrentData.Length == 0)
            {
                string cookieStatus = string.IsNullOrWhiteSpace(cookie) ? "no cookie" : "cookie present";
                return (null, null, null, $"failed to download torrent (null or empty), downloadUri={downloadUri}, {cookieStatus}");
            }

            if (!BaibakoParser.IsValidBencodedTorrent(torrentData))
            {
                return (torrentData, null, null, $"downloaded HTML instead of torrent file, downloadUri={downloadUri}");
            }

            string magnet = BencodeTo.Magnet(torrentData);
            string sizeName = BencodeTo.SizeName(torrentData);

            if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
            {
                return (torrentData, magnet, sizeName, null);
            }

            string errorDetails = $"magnet={(string.IsNullOrWhiteSpace(magnet) ? "null" : "ok")}, sizeName={(string.IsNullOrWhiteSpace(sizeName) ? "null" : "ok")}, torrentSize={torrentData.Length}";
            return (torrentData, null, null, $"failed to extract magnet or size: {errorDetails}");
        }
    }
}
