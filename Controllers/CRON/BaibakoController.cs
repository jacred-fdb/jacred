using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using System.Collections.Generic;
using JacRed.Engine;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/baibako/[action]")]
    public class BaibakoController : BaseController
    {
        #region Constants
        private static class BaibakoConstants
        {
            // Cookie names
            public const string COOKIE_PHPSESSID = "PHPSESSID";
            public const string COOKIE_PASS = "pass";
            public const string COOKIE_UID = "uid";

            // Endpoints
            public const string ENDPOINT_LOGIN = "/takelogin.php";
            public const string ENDPOINT_BROWSE = "/browse.php";
            public const string ENDPOINT_DOWNLOAD = "/download.php";

            // Cache keys
            public const string CACHE_COOKIE = "baibako:cookie";

            // Tracker name
            public const string TRACKER_NAME = "baibako";

            // Content types
            public const string TYPE_SERIAL = "serial";
            public const string TYPE_MOVIE = "movie";

            // Form parameters
            public const string PARAM_USERNAME = "username";
            public const string PARAM_PASSWORD = "password";

            // Validation
            public const string VALIDATION_NAV_TOP = "id=\"navtop\"";

            // Cache duration
            public static readonly TimeSpan COOKIE_CACHE_DURATION = TimeSpan.FromDays(1);
        }
        #endregion

        #region Compiled Regexes (Static cache to avoid recompilation)
        private static readonly Regex RegexSerialPattern1 = new Regex("/s\\d+e\\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RegexSerialPattern2 = new Regex("\\d+[\\-й]?\\s*сезон", RegexOptions.Compiled);
        private static readonly Regex RegexSerialPattern3 = new Regex("сезон\\s+повністю", RegexOptions.Compiled);
        private static readonly Regex RegexSerialPattern4 = new Regex("сезон\\s+полностью", RegexOptions.Compiled);
        private static readonly Regex RegexSerialPattern5 = new Regex("полный\\s+\\d+\\s+сезон", RegexOptions.Compiled);
        private static readonly Regex RegexSerialPattern6 = new Regex("повній\\s+\\d+[\\-й]?\\s*сезон", RegexOptions.Compiled);
        private static readonly Regex RegexSerialPattern7 = new Regex("\\d+[\\-й]?\\s*сезон\\s+повністю", RegexOptions.Compiled);
        private static readonly Regex RegexSerialPattern8 = new Regex("\\d+[\\-й]?\\s*сезон\\s+полностью", RegexOptions.Compiled);
        private static readonly Regex RegexSerialPattern9 = new Regex("сезон\\s+\\d+", RegexOptions.Compiled);
        private static readonly Regex RegexDownloadId = new Regex("href=[\"']/?(?:download\\.php\\?id=|download\\.php&amp;id=)([0-9]+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RegexCookieValue = new Regex("([^;]+)(;|$)", RegexOptions.Compiled);
        private static readonly Regex RegexWhitespace = new Regex("[\\n\\r\\t ]+", RegexOptions.Compiled);
        private static readonly Regex RegexTitleFormat = new Regex("([^/\\(]+)[^/]+/([^/\\(]+)", RegexOptions.Compiled);
        private static readonly Regex RegexQualityFilter = new Regex("(1080p|720p)", RegexOptions.Compiled);
        #endregion

        private static readonly SemaphoreSlim loginSemaphore = new SemaphoreSlim(1, 1);

        #region TakeLogin
        static string Cookie(IMemoryCache memoryCache)
        {
            // First check for static cookie from config
            if (!string.IsNullOrWhiteSpace(AppInit.conf.Baibako.cookie))
                return AppInit.conf.Baibako.cookie;

            // Then check cached cookie
            if (memoryCache.TryGetValue(BaibakoConstants.CACHE_COOKIE, out string cookie))
                return cookie;

            return null;
        }

        async Task<bool> CheckLogin()
        {
            // First check if we have a cookie (static from config or cached)
            if (Cookie(memoryCache) != null)
                return true;

            // If no cookie, try to login using credentials
            if (!string.IsNullOrWhiteSpace(AppInit.conf.Baibako.login?.u) &&
                !string.IsNullOrWhiteSpace(AppInit.conf.Baibako.login?.p))
            {
                return await TakeLogin();
            }

            // No cookie and no credentials
            ParserLog.Write(BaibakoConstants.TRACKER_NAME, "No cookie or login credentials available");
            return false;
        }

        async Task<bool> TakeLogin()
        {
            await loginSemaphore.WaitAsync();
            try
            {
                // Double-check after acquiring semaphore - another task might have logged in
                if (Cookie(memoryCache) != null)
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
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                    var postParams = new Dictionary<string, string>
                    {
                        { BaibakoConstants.PARAM_USERNAME, login },
                        { BaibakoConstants.PARAM_PASSWORD, pass }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{host}{BaibakoConstants.ENDPOINT_LOGIN}", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string sessid = ExtractCookieValue(cook, BaibakoConstants.COOKIE_PHPSESSID);
                                string passCookie = ExtractCookieValue(cook, BaibakoConstants.COOKIE_PASS);
                                string uid = ExtractCookieValue(cook, BaibakoConstants.COOKIE_UID);

                                if (!string.IsNullOrWhiteSpace(sessid) && !string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(passCookie))
                                {
                                    string cookieStr = $"{BaibakoConstants.COOKIE_PHPSESSID}={sessid}; {BaibakoConstants.COOKIE_UID}={uid}; {BaibakoConstants.COOKIE_PASS}={passCookie}";
                                    memoryCache.Set(BaibakoConstants.CACHE_COOKIE, cookieStr, BaibakoConstants.COOKIE_CACHE_DURATION);
                                    ParserLog.Write(BaibakoConstants.TRACKER_NAME, "Login OK");
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ParserLog.Write(BaibakoConstants.TRACKER_NAME, $"Login error: {ex.Message}");
            }
            finally
            {
                loginSemaphore.Release();
            }

            return false;
        }

        private string ExtractCookieValue(IEnumerable<string> cookieHeaders, string cookieName)
        {
            foreach (string line in cookieHeaders)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains($"{cookieName}="))
                    continue;

                var match = RegexCookieValue.Match(line.Substring(line.IndexOf($"{cookieName}=") + cookieName.Length + 1));
                if (match.Success)
                    return match.Groups[1].Value;
            }
            return null;
        }
        #endregion


        #region Parse
        static bool workParse = false;

        /// <summary>
        /// Parses torrent releases from Baibako website pages.
        /// </summary>
        /// <param name="parseFrom">The starting page number to parse from. If 0 or less, defaults to page 0.</param>
        /// <param name="parseTo">The ending page number to parse to. If 0 or less, defaults to the same value as parseFrom.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a status string:
        /// - "work" if parsing is already in progress
        /// - "disabled" if host is not configured
        /// - "login error" if authorization failed
        /// - "ok" if parsing completed successfully
        /// </returns>
        [HttpGet]
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            if (workParse)
                return "work";

            if (string.IsNullOrEmpty(AppInit.conf.Baibako.host))
                return "disabled";

            workParse = true;

            try
            {
                #region Авторизация
                if (!await CheckLogin())
                    return "login error";
                #endregion

                var sw = Stopwatch.StartNew();
                string baseUrl = $"{AppInit.conf.Baibako.host}{BaibakoConstants.ENDPOINT_BROWSE}";

                // Determine page range
                int startPage = parseFrom >= 0 ? parseFrom : 0;
                int endPage = parseTo >= 0 ? parseTo : (parseFrom >= 0 ? parseFrom : 0);

                // Ensure startPage <= endPage
                if (startPage > endPage)
                {
                    int temp = startPage;
                    startPage = endPage;
                    endPage = temp;
                }

                ParserLog.Write(BaibakoConstants.TRACKER_NAME, $"Starting parse", new Dictionary<string, object>
                {
                    { "parseFrom", parseFrom },
                    { "parseTo", parseTo },
                    { "startPage", startPage },
                    { "endPage", endPage },
                    { "baseUrl", baseUrl }
                });

                int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                // Parse pages from startPage to endPage
                for (int page = startPage; page <= endPage; page++)
                {
                    if (page > startPage)
                        await Task.Delay(AppInit.conf.Baibako.parseDelay);

                    ParserLog.Write(BaibakoConstants.TRACKER_NAME, $"Page {page}: {baseUrl}?page={page}");
                    var result = await parsePage(page);
                    totalParsed += result.parsed;
                    totalAdded += result.added;
                    totalUpdated += result.updated;
                    totalSkipped += result.skipped;
                    totalFailed += result.failed;
                }

                ParserLog.Write(BaibakoConstants.TRACKER_NAME, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
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
                ParserLog.Write(BaibakoConstants.TRACKER_NAME, $"Error: {ex.Message}");
            }
            finally
            {
                workParse = false;
            }

            return "ok";
        }
        #endregion


        #region parsePage
        async Task<(int parsed, int added, int updated, int skipped, int failed)> parsePage(int page)
        {
            string html = await HttpClient.Get($"{AppInit.conf.Baibako.host}{BaibakoConstants.ENDPOINT_BROWSE}?page={page}", encoding: Encoding.GetEncoding(1251), cookie: Cookie(memoryCache));
            if (html == null || !html.Contains(BaibakoConstants.VALIDATION_NAV_TOP))
            {
                ParserLog.Write(BaibakoConstants.TRACKER_NAME, $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "reason", html == null ? "null response" : "invalid content" }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = new List<BaibakoDetails>();

            foreach (string row in tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", ""))).Split("<tr").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;

                // Дата создания
                DateTime createTime = tParse.ParseCreateTime(ExtractAndClean(row, "<small>(?:Загружена|Обновлена): ([0-9]+ [^ ]+ [0-9]{4}) в [^<]+</small>"), "dd.MM.yyyy");
                if (createTime == default)
                {
                    if (page != 0)
                        continue;

                    createTime = DateTime.UtcNow;
                }

                #region Данные раздачи
                var gurl = Regex.Match(row, "<a href=\"/?(details.php\\?id=[0-9]+)[^\"]+\">([^<]+)</a>").Groups;

                string url = gurl[1].Value;
                string title = gurl[2].Value;

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
                    continue;

                title = title.Replace("(Обновляемая)", "").Replace("(Золото)", "").Replace("(Оновлюється)", "");
                title = Regex.Replace(title, "/( +| )?$", "").Trim();

                // Filter by quality - only accept 1080p or 720p releases
                if (!RegexQualityFilter.IsMatch(title))
                    continue;

                url = $"{AppInit.conf.Baibako.host}/{url}";
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                var g = RegexTitleFormat.Match(title).Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[1].Value.Trim();
                    originalname = g[2].Value.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    // Extract download ID from href="download.php?id=42075" or href="download.php&amp;id=42075"
                    var downloadMatch = RegexDownloadId.Match(row);
                    if (!downloadMatch.Success)
                        continue;

                    string downloadId = downloadMatch.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(downloadId))
                        continue;

                    #region types
                    string[] types = DetectContentType(title);
                    #endregion

                    torrents.Add(new BaibakoDetails()
                    {
                        trackerName = BaibakoConstants.TRACKER_NAME,
                        types = types,
                        url = url,
                        title = title,
                        sid = 1,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        downloadUri = $"{AppInit.conf.Baibako.host}{BaibakoConstants.ENDPOINT_DOWNLOAD}?id={downloadId}"
                    });
                }
            }

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
                        // Get cookie once for this torrent
                        string cookie = Cookie(memoryCache);
                        string referer = $"{AppInit.conf.Baibako.host}{BaibakoConstants.ENDPOINT_BROWSE}";

                        // Check if already exists
                        bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);

                        // If torrent exists with same title, check if we need to update
                        if (exists && string.Equals(_tcache.title?.Trim(), t.title?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            // Check if types changed
                            bool typesChanged = !TypesEqual(t.types, _tcache.types);

                            // If types changed, update without downloading torrent
                            if (typesChanged)
                            {
                                updatedCount++;
                                ParserLog.WriteUpdated(BaibakoConstants.TRACKER_NAME, t, $"types updated: [{string.Join(", ", _tcache.types ?? new string[0])}] -> [{string.Join(", ", t.types ?? new string[0])}]");
                                return true;
                            }

                            // Download and extract magnet/size for existing torrent
                            var extractResult = await DownloadAndExtractTorrent(t.downloadUri, cookie, referer);
                            if (extractResult.error != null)
                            {
                                skippedCount++;
                                ParserLog.WriteSkipped(BaibakoConstants.TRACKER_NAME, _tcache, extractResult.error);
                                return false;
                            }

                            // Check if magnet or size changed
                            string magnetCompare = _tcache.magnet?.Trim() ?? "";
                            string sizeCompare = _tcache.sizeName?.Trim() ?? "";
                            string newMagnetCompare = extractResult.magnet?.Trim() ?? "";
                            string newSizeCompare = extractResult.sizeName?.Trim() ?? "";

                            bool magnetChanged = !string.Equals(magnetCompare, newMagnetCompare, StringComparison.OrdinalIgnoreCase);
                            bool sizeChanged = !string.Equals(sizeCompare, newSizeCompare, StringComparison.OrdinalIgnoreCase);

                            if (!magnetChanged && !sizeChanged)
                            {
                                skippedCount++;
                                ParserLog.WriteSkipped(BaibakoConstants.TRACKER_NAME, _tcache, "no changes");
                                return false;
                            }

                            // Update with new magnet/size
                            t.magnet = extractResult.magnet;
                            t.sizeName = extractResult.sizeName;
                            updatedCount++;
                            string reason = magnetChanged && sizeChanged ? "magnet and size updated" : (magnetChanged ? "magnet updated" : "size updated");
                            ParserLog.WriteUpdated(BaibakoConstants.TRACKER_NAME, t, reason);
                            return true;
                        }

                        // New torrent or title changed - download and add/update
                        var result = await DownloadAndExtractTorrent(t.downloadUri, cookie, referer);
                        if (result.error != null)
                        {
                            failedCount++;
                            ParserLog.WriteFailed(BaibakoConstants.TRACKER_NAME, t, result.error);
                            return false;
                        }

                        t.magnet = result.magnet;
                        t.sizeName = result.sizeName;

                        if (exists)
                        {
                            updatedCount++;
                            ParserLog.WriteUpdated(BaibakoConstants.TRACKER_NAME, t, "title changed or new data");
                        }
                        else
                        {
                            addedCount++;
                            ParserLog.WriteAdded(BaibakoConstants.TRACKER_NAME, t);
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        ParserLog.WriteFailed(BaibakoConstants.TRACKER_NAME, t, $"exception: {ex.Message}");
                        return false;
                    }
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write(BaibakoConstants.TRACKER_NAME, $"Page {page} completed",
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

        private string ExtractAndClean(string text, string pattern)
        {
            string res = new Regex(pattern, RegexOptions.IgnoreCase).Match(text).Groups[1].Value.Trim();
            res = RegexWhitespace.Replace(res, " ");
            return res.Trim();
        }

        private string[] DetectContentType(string title)
        {
            string titleLower = title.ToLower();

            bool isSerial = RegexSerialPattern1.IsMatch(title) ||
                           RegexSerialPattern2.IsMatch(titleLower) ||
                           RegexSerialPattern3.IsMatch(titleLower) ||
                           RegexSerialPattern4.IsMatch(titleLower) ||
                           RegexSerialPattern5.IsMatch(titleLower) ||
                           RegexSerialPattern6.IsMatch(titleLower) ||
                           RegexSerialPattern7.IsMatch(titleLower) ||
                           RegexSerialPattern8.IsMatch(titleLower) ||
                           RegexSerialPattern9.IsMatch(titleLower);

            return isSerial
                ? new string[] { BaibakoConstants.TYPE_SERIAL }
                : new string[] { BaibakoConstants.TYPE_MOVIE };
        }

        private bool TypesEqual(string[] types1, string[] types2)
        {
            if (types1 == null && types2 == null) return true;
            if (types1 == null || types2 == null) return false;
            return types1.SequenceEqual(types2 ?? new string[0]);
        }

        private async Task<(byte[] data, string magnet, string sizeName, string error)>
            DownloadAndExtractTorrent(string downloadUri, string cookie, string referer)
        {
            byte[] torrentData = await HttpClient.Download(downloadUri, cookie: cookie, referer: referer);

            if (torrentData == null || torrentData.Length == 0)
            {
                string cookieStatus = string.IsNullOrWhiteSpace(cookie) ? "no cookie" : "cookie present";
                return (null, null, null, $"failed to download torrent (null or empty), downloadUri={downloadUri}, {cookieStatus}");
            }

            // Check if downloaded data looks like a torrent file
            if (!IsValidBencodedTorrent(torrentData))
            {
                return (torrentData, null, null, $"downloaded HTML instead of torrent file, downloadUri={downloadUri}");
            }

            string magnet = BencodeTo.Magnet(torrentData);
            string sizeName = BencodeTo.SizeName(torrentData);

            if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
            {
                return (torrentData, magnet, sizeName, null);
            }

            string errorDetails = $"magnet={(string.IsNullOrWhiteSpace(magnet) ? "null" : "ok")}, sizeName={(string.IsNullOrWhiteSpace(sizeName) ? "null" : "ok")}, torrentSize={torrentData?.Length ?? 0}";
            return (torrentData, null, null, $"failed to extract magnet or size: {errorDetails}");
        }

        private bool IsValidBencodedTorrent(byte[] data)
        {
            if (data == null || data.Length == 0)
                return false;

            // Valid bencoded torrent starts with 'd' (dictionary)
            if (data[0] == (byte)'d')
                return true;

            // If small and looks like HTML, it's not a torrent
            if (data.Length < 100)
            {
                string preview = Encoding.UTF8.GetString(data, 0, Math.Min(200, data.Length));
                if (preview.Contains("<html") || preview.Contains("<!DOCTYPE") || preview.Contains("<body"))
                    return false;
            }

            return false;
        }
        #endregion
    }
}
