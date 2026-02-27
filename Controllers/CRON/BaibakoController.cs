using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        #region TakeLogin
        static string Cookie(IMemoryCache memoryCache)
        {
            // First check for static cookie from config
            if (!string.IsNullOrWhiteSpace(AppInit.conf.Baibako.cookie))
                return AppInit.conf.Baibako.cookie;

            // Then check cached cookie
            if (memoryCache.TryGetValue("baibako:cookie", out string cookie))
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
            ParserLog.Write("baibako", "No cookie or login credentials available");
            return false;
        }

        async Task<bool> TakeLogin()
        {
            try
            {
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
                        { "username", login },
                        { "password", pass }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{host}/takelogin.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string sessid = null, passCookie = null, uid = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("PHPSESSID="))
                                        sessid = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("pass="))
                                        passCookie = new Regex("pass=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("uid="))
                                        uid = new Regex("uid=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(sessid) && !string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(passCookie))
                                {
                                    string cookieStr = $"PHPSESSID={sessid}; uid={uid}; pass={passCookie}";
                                    memoryCache.Set("baibako:cookie", cookieStr, TimeSpan.FromDays(1));
                                    ParserLog.Write("baibako", "Login OK");
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ParserLog.Write("baibako", $"Login error: {ex.Message}");
            }

            return false;
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
                string baseUrl = $"{AppInit.conf.Baibako.host}/browse.php";

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

                ParserLog.Write("baibako", $"Starting parse", new Dictionary<string, object>
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

                    ParserLog.Write("baibako", $"Page {page}: {baseUrl}?page={page}");
                    var result = await parsePage(page);
                    totalParsed += result.parsed;
                    totalAdded += result.added;
                    totalUpdated += result.updated;
                    totalSkipped += result.skipped;
                    totalFailed += result.failed;
                }

                ParserLog.Write("baibako", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
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
                ParserLog.Write("baibako", $"Error: {ex.Message}");
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
            string html = await HttpClient.Get($"{AppInit.conf.Baibako.host}/browse.php?page={page}", encoding: Encoding.GetEncoding(1251), cookie: Cookie(memoryCache));
            if (html == null || !html.Contains("id=\"navtop\""))
            {
                ParserLog.Write("baibako", $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "reason", html == null ? "null response" : "invalid content" }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = new List<BaibakoDetails>();

            foreach (string row in tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", ""))).Split("<tr").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim();
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                // Дата создания
                // HTML uses "Обновлена" (Updated) or "Загружена" (Uploaded)
                // Note: Some dates may be in the future (2025-2026) if tracker uses release dates for upcoming content
                DateTime createTime = tParse.ParseCreateTime(Match("<small>(?:Загружена|Обновлена): ([0-9]+ [^ ]+ [0-9]{4}) в [^<]+</small>"), "dd.MM.yyyy");
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
                if (!Regex.IsMatch(title, "(1080p|720p)"))
                    continue;

                url = $"{AppInit.conf.Baibako.host}/{url}";
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                // 9-1-1 /9-1-1 /s04e01-13 /WEBRip XviD
                var g = Regex.Match(title, "([^/\\(]+)[^/]+/([^/\\(]+)").Groups;
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
                    // Match both regular and HTML-encoded ampersand
                    var downloadMatch = Regex.Match(row, "href=[\"']/?(?:download\\.php\\?id=|download\\.php&amp;id=)([0-9]+)[\"']", RegexOptions.IgnoreCase);
                    if (!downloadMatch.Success)
                        continue;

                    string downloadId = downloadMatch.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(downloadId))
                        continue;

                    #region types
                    // Determine content type based on title patterns
                    // Serials typically have: s01e01, s02e01-10, сезон, Сезон, сезон повністю, etc.
                    string[] types = null;
                    string titleLower = title.ToLower();

                    // Check for serial patterns (season/episode indicators)
                    // Patterns: s01e01, s02e01-10, /сезон, /Сезон, сезон повністю, полностью, etc.
                    bool isSerial = Regex.IsMatch(title, "/s\\d+e\\d+", RegexOptions.IgnoreCase) ||
                                    Regex.IsMatch(titleLower, "\\d+[\\-й]?\\s*сезон") ||
                                    Regex.IsMatch(titleLower, "сезон\\s+повністю") ||
                                    Regex.IsMatch(titleLower, "сезон\\s+полностью") ||
                                    Regex.IsMatch(titleLower, "полный\\s+\\d+\\s+сезон") ||
                                    Regex.IsMatch(titleLower, "повний\\s+\\d+[\\-й]?\\s*сезон") ||
                                    Regex.IsMatch(titleLower, "\\d+[\\-й]?\\s*сезон\\s+повністю") ||
                                    Regex.IsMatch(titleLower, "\\d+[\\-й]?\\s*сезон\\s+полностью") ||
                                    Regex.IsMatch(titleLower, "сезон\\s+\\d+") ||
                                    Regex.IsMatch(titleLower, "s\\d+e\\d+");

                    if (isSerial)
                    {
                        types = new string[] { "serial" };
                    }
                    else
                    {
                        // If no serial patterns found, assume it's a movie
                        types = new string[] { "movie" };
                    }
                    #endregion

                    torrents.Add(new BaibakoDetails()
                    {
                        trackerName = "baibako",
                        types = types,
                        url = url,
                        title = title,
                        sid = 1,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        downloadUri = $"{AppInit.conf.Baibako.host}/download.php?id={downloadId}"
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

                        // Check if already exists
                        bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);

                        // If torrent exists with same title, check if we need to update
                        if (exists && string.Equals(_tcache.title?.Trim(), t.title?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            // Check if types changed (e.g., serial -> movie or vice versa)
                            bool typesChanged = false;
                            if (t.types != null && _tcache.types != null)
                            {
                                // Compare types arrays
                                if (t.types.Length != _tcache.types.Length)
                                {
                                    typesChanged = true;
                                }
                                else
                                {
                                    foreach (string type in t.types)
                                    {
                                        if (!_tcache.types.Contains(type))
                                        {
                                            typesChanged = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            else if (t.types != null || _tcache.types != null)
                            {
                                // One is null, other is not
                                typesChanged = true;
                            }

                            // If types changed, update without downloading torrent
                            if (typesChanged)
                            {
                                updatedCount++;
                                ParserLog.WriteUpdated("baibako", t, $"types updated: [{string.Join(", ", _tcache.types ?? new string[0])}] -> [{string.Join(", ", t.types ?? new string[0])}]");
                                return true;
                            }

                            // Download torrent to get current magnet and size
                            byte[] torrent = await HttpClient.Download(t.downloadUri, cookie: cookie, referer: $"{AppInit.conf.Baibako.host}/browse.php");

                            if (torrent == null || torrent.Length == 0)
                            {
                                skippedCount++;
                                string cookieStatus = string.IsNullOrWhiteSpace(cookie) ? "no cookie" : "cookie present";
                                ParserLog.WriteSkipped("baibako", _tcache, $"failed to download torrent (null or empty), downloadUri={t.downloadUri}, {cookieStatus}");
                                return false;
                            }

                            string magnet = BencodeTo.Magnet(torrent);
                            string sizeName = BencodeTo.SizeName(torrent);

                            if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
                            {
                                // Check if magnet or size changed
                                bool magnetChanged = !string.Equals(_tcache.magnet?.Trim(), magnet?.Trim(), StringComparison.OrdinalIgnoreCase);
                                bool sizeChanged = !string.Equals(_tcache.sizeName?.Trim(), sizeName?.Trim(), StringComparison.OrdinalIgnoreCase);

                                if (!magnetChanged && !sizeChanged)
                                {
                                    // No changes detected, skip
                                    skippedCount++;
                                    ParserLog.WriteSkipped("baibako", _tcache, "no changes");
                                    return false;
                                }

                                // Update with new magnet/size
                                t.magnet = magnet;
                                t.sizeName = sizeName;
                                updatedCount++;
                                string reason = magnetChanged && sizeChanged ? "magnet and size updated" : (magnetChanged ? "magnet updated" : "size updated");
                                ParserLog.WriteUpdated("baibako", t, reason);
                                return true;
                            }
                            else
                            {
                                // Failed to extract magnet/size, but torrent exists - skip to avoid overwriting with empty data
                                skippedCount++;
                                ParserLog.WriteSkipped("baibako", _tcache, "failed to extract magnet/size, keeping existing data");
                                return false;
                            }
                        }

                        // New torrent or title changed - download and add/update
                        byte[] torrentData = await HttpClient.Download(t.downloadUri, cookie: cookie, referer: $"{AppInit.conf.Baibako.host}/browse.php");

                        if (torrentData == null || torrentData.Length == 0)
                        {
                            failedCount++;
                            string cookieStatus = string.IsNullOrWhiteSpace(cookie) ? "no cookie" : "cookie present";
                            ParserLog.WriteFailed("baibako", t, $"failed to download torrent (null or empty), downloadUri={t.downloadUri}, {cookieStatus}");
                            return false;
                        }

                        // Check if downloaded data looks like a torrent file (should start with "d" for bencoded dictionary)
                        // Torrent files are bencoded and start with 'd' (dictionary) or sometimes have BOM
                        bool looksLikeTorrent = torrentData.Length > 0 && torrentData[0] == (byte)'d';
                        if (!looksLikeTorrent && torrentData.Length < 100)
                        {
                            // Might be HTML error page - check if it contains HTML tags
                            string preview = Encoding.UTF8.GetString(torrentData, 0, Math.Min(200, torrentData.Length));
                            if (preview.Contains("<html") || preview.Contains("<!DOCTYPE") || preview.Contains("<body"))
                            {
                                failedCount++;
                                ParserLog.WriteFailed("baibako", t, $"downloaded HTML instead of torrent file, downloadUri={t.downloadUri}, preview={preview.Substring(0, Math.Min(100, preview.Length))}");
                                return false;
                            }
                        }

                        string newMagnet = BencodeTo.Magnet(torrentData);
                        string newSizeName = BencodeTo.SizeName(torrentData);

                        if (!string.IsNullOrWhiteSpace(newMagnet) && !string.IsNullOrWhiteSpace(newSizeName))
                        {
                            t.magnet = newMagnet;
                            t.sizeName = newSizeName;

                            if (exists)
                            {
                                updatedCount++;
                                ParserLog.WriteUpdated("baibako", t, "title changed or new data");
                            }
                            else
                            {
                                addedCount++;
                                ParserLog.WriteAdded("baibako", t);
                            }

                            return true;
                        }

                        // More detailed error message
                        string errorDetails = $"magnet={(string.IsNullOrWhiteSpace(newMagnet) ? "null" : "ok")}, sizeName={(string.IsNullOrWhiteSpace(newSizeName) ? "null" : "ok")}, torrentSize={torrentData?.Length ?? 0}";
                        failedCount++;
                        ParserLog.WriteFailed("baibako", t, $"failed to extract magnet or size: {errorDetails}");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        ParserLog.WriteFailed("baibako", t, $"exception: {ex.Message}");
                        return false;
                    }
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write("baibako", $"Page {page} completed",
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
        #endregion
    }
}
