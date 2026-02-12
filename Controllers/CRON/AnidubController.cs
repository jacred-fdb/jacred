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

namespace JacRed.Controllers.CRON
{
    [Route("/cron/anidub/[action]")]
    public class AnidubController : BaseController
    {
        #region Parse
        static volatile bool workParse = false;
        private static readonly object workParseLock = new object();

        /// <summary>
        /// Attempts to start a parse operation. Returns true if successful, false if parsing is already in progress.
        /// </summary>
        /// <returns>True if parse was started, false if already running.</returns>
        private static bool TryStartParse()
        {
            lock (workParseLock)
            {
                if (workParse)
                    return false;

                workParse = true;
                return true;
            }
        }

        /// <summary>
        /// Ends the parse operation, allowing a new parse to start.
        /// </summary>
        private static void EndParse()
        {
            lock (workParseLock)
            {
                workParse = false;
            }
        }

        /// <summary>
        /// Parses torrent releases from Anidub website pages.
        /// </summary>
        /// <param name="parseFrom">The starting page number to parse from. If 0 or less, defaults to page 1.</param>
        /// <param name="parseTo">The ending page number to parse to. If 0 or less, defaults to the same value as parseFrom.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a status string:
        /// - "work" if parsing is already in progress
        /// - "canceled" if the operation was canceled
        /// - "ok" if parsing completed successfully
        /// </returns>
        [HttpGet]
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            if (!TryStartParse())
                return "work";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = AppInit.conf.Anidub.host;

                // Determine page range
                int startPage = parseFrom > 0 ? parseFrom : 1;
                int endPage = parseTo > 0 ? parseTo : (parseFrom > 0 ? parseFrom : 1);

                // Ensure startPage <= endPage
                if (startPage > endPage)
                {
                    int temp = startPage;
                    startPage = endPage;
                    endPage = temp;
                }

                ParserLog.Write("anidub", $"Starting parse", new Dictionary<string, object>
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
                        await Task.Delay(AppInit.conf.Anidub.parseDelay);

                    if (page > 1)
                    {
                        ParserLog.Write("anidub", $"Parsing page", new Dictionary<string, object>
                        {
                            { "page", page },
                            { "url", $"{baseUrl}/page/{page}/" }
                        });
                    }

                    (int parsed, int added, int updated, int skipped, int failed) = await parsePage(page);
                    totalParsed += parsed;
                    totalAdded += added;
                    totalUpdated += updated;
                    totalSkipped += skipped;
                    totalFailed += failed;
                }

                ParserLog.Write("anidub", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                    new Dictionary<string, object>
                    {
                        { "parsed", totalParsed },
                        { "added", totalAdded },
                        { "updated", totalUpdated },
                        { "skipped", totalSkipped },
                        { "failed", totalFailed }
                    });
            }
            catch (OperationCanceledException oce)
            {
                ParserLog.Write("anidub", $"Canceled", new Dictionary<string, object>
                {
                    { "message", oce.Message },
                    { "stackTrace", oce.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
                return "canceled";
            }
            catch (Exception ex)
            {
                // Rethrow critical exceptions that should never be swallowed
                if (ex is OutOfMemoryException)
                    throw;

                ParserLog.Write("anidub", $"Error", new Dictionary<string, object>
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
        /// <summary>
        /// Extracts the release year from detail page HTML.
        /// </summary>
        /// <param name="html">The HTML content of the detail page.</param>
        /// <returns>The release year as an integer, or 0 if not found.</returns>
        static int ExtractRelased(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return 0;

            // Try pattern: <b>Год: </b><span><a href="...">2026</a></span>
            var yearMatch = Regex.Match(html, @"<b>Год:\s*</b>\s*<span>\s*<a[^>]*>([0-9]{4})</a>\s*</span>", RegexOptions.IgnoreCase);
            if (!yearMatch.Success)
            {
                // Try pattern: <b>Год: </b><span>2026</span>
                yearMatch = Regex.Match(html, @"<b>Год:\s*</b>\s*<span>([0-9]{4})</span>", RegexOptions.IgnoreCase);
            }

            if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int year) && year > 1900 && year <= DateTime.UtcNow.Year + 1)
            {
                return year;
            }

            return 0;
        }

        /// <summary>
        /// Parses a single page of torrent releases from the Anidub website.
        /// </summary>
        /// <param name="page">The page number to parse. Page 1 uses the base URL, other pages use the /page/{page}/ format.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a tuple with parsing statistics:
        /// - parsed: Total number of torrent releases found and processed
        /// - added: Number of new torrent releases added to the database
        /// - updated: Number of existing torrent releases that were updated
        /// - skipped: Number of torrent releases skipped (no changes detected)
        /// - failed: Number of torrent releases that failed to process
        /// </returns>
        async Task<(int parsed, int added, int updated, int skipped, int failed)> parsePage(int page)
        {
            string url = page == 1 ? AppInit.conf.Anidub.host : $"{AppInit.conf.Anidub.host}/page/{page}/";
            string html = await HttpClient.Get(url, encoding: Encoding.UTF8, useproxy: AppInit.conf.Anidub.useproxy);

            if (html == null || !html.Contains("dle-content"))
            {
                ParserLog.Write("anidub", $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", url },
                    { "reason", html == null ? "null response" : "invalid content" }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = new List<AnidubDetails>();

            // Parse releases from the main content area
            // Looking for article blocks or div blocks with story/release information
            // The main page has releases listed, we need to find the pattern
            // Try multiple patterns: article tags, div blocks with links, etc.
            var rows = html.Contains("<article")
                ? tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html)).Split("<article").Skip(1)
                : tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html)).Split(new[] { "<div class=\"story", "<div class=\"rand", "<li><a href=\"" }, StringSplitOptions.None).Skip(1);

            foreach (string row in rows)
            {
                #region Match
                string Match(string pattern, int index = 1)
                {
                    string res = new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim();
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                // Skip if not a valid release block
                // Look for release links (containing .html and not being navigation links)
                if (!row.Contains("href=\"") || !row.Contains(".html"))
                    continue;

                #region createTime
                DateTime createTime = default;

                // Try to parse date from various formats
                string dateStr = Match("<li><b>Дата:</b> ([^<]+)</li>");
                if (!string.IsNullOrWhiteSpace(dateStr))
                {
                    // Format: "9-02-2026, 05:55" or "Сегодня, 05:55" or "Вчера, 05:55"
                    if (dateStr.Contains("Сегодня"))
                    {
                        createTime = DateTime.UtcNow;
                    }
                    else if (dateStr.Contains("Вчера"))
                    {
                        createTime = DateTime.UtcNow.AddDays(-1);
                    }
                    else
                    {
                        // Try parsing date formats like "9-02-2026" or "09-02-2026"
                        var dateMatch = Regex.Match(dateStr, "([0-9]{1,2})-([0-9]{2})-([0-9]{4})");
                        if (dateMatch.Success)
                        {
                            string day = dateMatch.Groups[1].Value.PadLeft(2, '0');
                            string month = dateMatch.Groups[2].Value;
                            string year = dateMatch.Groups[3].Value;
                            createTime = tParse.ParseCreateTime($"{day}.{month}.{year}", "dd.MM.yyyy");
                        }
                    }
                }

                if (createTime == default)
                {
                    if (page != 1)
                        continue;
                    createTime = DateTime.UtcNow;
                }
                #endregion

                #region URL и title
                // Extract URL and title from article link
                var gurl = Regex.Match(row, "<a href=\"([^\"]+)\"[^>]*>([^<]+)</a>");
                if (!gurl.Success)
                    continue;

                string urlPath = gurl.Groups[1].Value;
                string title = gurl.Groups[2].Value;

                // Skip if URL is not a valid release URL
                if (string.IsNullOrWhiteSpace(urlPath) || string.IsNullOrWhiteSpace(title))
                    continue;

                // Filter out navigation and other non-release links
                if (urlPath.Contains("/user/") || urlPath.Contains("/xfsearch/") ||
                    urlPath.Contains("/forum/") || urlPath.Contains("javascript:") ||
                    urlPath.StartsWith("#") || !urlPath.Contains(".html"))
                    continue;

                // Build full URL
                string fullUrl = urlPath.StartsWith("http") ? urlPath : $"{AppInit.conf.Anidub.host}/{urlPath.TrimStart('/')}";

                // Clean title
                title = HttpUtility.HtmlDecode(title).Trim();
                title = Regex.Replace(title, "[\n\r\t ]+", " ").Trim();

                if (string.IsNullOrWhiteSpace(title))
                    continue;
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                // Try to extract name and original name from title
                // Format: "Название / Original Name [XX из YY]"
                var nameMatch = Regex.Match(title, "^([^/]+)\\s*/\\s*([^\\[]+)(?:\\s*\\[|$)");
                if (nameMatch.Success)
                {
                    name = nameMatch.Groups[1].Value.Trim();
                    originalname = nameMatch.Groups[2].Value.Trim();
                }
                else
                {
                    // If no slash, try to extract just the name before brackets
                    var simpleMatch = Regex.Match(title, "^([^\\[]+)(?:\\s*\\[|$)");
                    if (simpleMatch.Success)
                    {
                        name = simpleMatch.Groups[1].Value.Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();
                #endregion

                #region Extract download URI
                // The download link will be parsed from the detail page
                // For now, set it to the detail page URL - we'll extract the actual download link when parsing
                string downloadUri = fullUrl;
                #endregion

                #region Determine types
                string[] types = new string[] { "anime" }; // Default

                // Determine type from URL or category
                if (urlPath.Contains("/dorama/"))
                    types = new string[] { "dorama" };
                else if (urlPath.Contains("/anime_movie/") || urlPath.Contains("/anime-movie/"))
                    types = new string[] { "anime", "movie" };
                else if (urlPath.Contains("/anime_ova/") || urlPath.Contains("/anime-ova/"))
                    types = new string[] { "anime", "ova" };
                else if (urlPath.Contains("/anime_tv/") || urlPath.Contains("/anime-tv/"))
                    types = new string[] { "anime", "serial" };
                #endregion

                torrents.Add(new AnidubDetails()
                {
                    trackerName = "anidub",
                    types = types,
                    url = fullUrl,
                    title = title,
                    sid = 1,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    downloadUri = downloadUri
                });
            }

            int parsedCount = torrents.Count;
            int addedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            // If we found torrents, process them
            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, async (t, db) =>
                {
                    // Check if already exists
                    bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);
                    string detailHtml = null; // Will be reused if already fetched

                    // If torrent exists with same title, verify magnet hasn't changed before skipping
                    if (exists && string.Equals(_tcache.title?.Trim(), t.title?.Trim(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_tcache.magnet))
                    {
                        // Check magnet link from detail page to verify it hasn't changed
                        // This is a lightweight check to avoid skipping when magnet has changed
                        detailHtml = await HttpClient.Get(t.url, encoding: Encoding.UTF8, useproxy: AppInit.conf.Anidub.useproxy);
                        if (detailHtml != null)
                        {
                            // Extract relased from detail page
                            int relased = ExtractRelased(detailHtml);
                            if (relased > 0)
                                t.relased = relased;

                            var magnetMatch = Regex.Match(detailHtml, "href=\"(magnet:\\?[^\"]+)\"", RegexOptions.IgnoreCase);
                            if (magnetMatch.Success)
                            {
                                string currentMagnet = magnetMatch.Groups[1].Value;
                                // Only skip if magnet link hasn't changed and relased is already set (or both are 0)
                                bool shouldSkip = string.Equals(_tcache.magnet, currentMagnet, StringComparison.OrdinalIgnoreCase);
                                
                                // If existing entry has relased = 0 but we found a year, we should update
                                if (shouldSkip && _tcache.relased == 0 && relased > 0)
                                {
                                    shouldSkip = false;
                                    t.magnet = currentMagnet;
                                    t.relased = relased;
                                    
                                    // Try to get size from detail page
                                    var sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                                    if (!sizeMatch.Success)
                                        sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*([^<]+)", RegexOptions.IgnoreCase);
                                    if (sizeMatch.Success)
                                        t.sizeName = HttpUtility.HtmlDecode(sizeMatch.Groups[1].Value).Trim();
                                    
                                    updatedCount++;
                                    ParserLog.WriteUpdated("anidub", t, "relased updated");
                                    return true;
                                }
                                
                                if (shouldSkip)
                                {
                                    skippedCount++;
                                    ParserLog.WriteSkipped("anidub", _tcache, "no changes");
                                    return false; // Skip processing this torrent
                                }
                                // If magnet changed, process detailHtml immediately to avoid redundant download attempt
                                t.magnet = currentMagnet;
                                
                                // Try to get size from detail page
                                var sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                                if (!sizeMatch.Success)
                                    sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*([^<]+)", RegexOptions.IgnoreCase);
                                if (sizeMatch.Success)
                                    t.sizeName = HttpUtility.HtmlDecode(sizeMatch.Groups[1].Value).Trim();
                                
                                // Try download.php link if available for accurate sizeName
                                var downloadMatch = Regex.Match(detailHtml, "href=\"([^\"]*engine/download\\.php\\?id=[0-9]+)\"", RegexOptions.IgnoreCase);
                                if (downloadMatch.Success)
                                {
                                    string downloadUrl = downloadMatch.Groups[1].Value;
                                    if (!downloadUrl.StartsWith("http"))
                                        downloadUrl = $"{AppInit.conf.Anidub.host}/{downloadUrl.TrimStart('/')}";
                                    
                                    byte[] torrentFile = await HttpClient.Download(downloadUrl, referer: t.url, useproxy: AppInit.conf.Anidub.useproxy);
                                    if (torrentFile != null && torrentFile.Length > 0)
                                    {
                                        string sizeName = BencodeTo.SizeName(torrentFile);
                                        if (!string.IsNullOrWhiteSpace(sizeName))
                                            t.sizeName = sizeName;
                                    }
                                }
                                
                                // Update with changed magnet
                                updatedCount++;
                                ParserLog.WriteUpdated("anidub", t, "magnet changed");
                                return true;
                            }
                            // If we couldn't verify magnet from detail page, continue with normal flow
                        }
                    }

                    // Try to download torrent file from downloadUri (if it's a direct download link)
                    // Note: downloadUri is set to fullUrl (detail page) by default, so this will likely fail
                    // and we'll fall back to parsing the detail page
                    // Skip this if we already have detailHtml (to avoid redundant download attempt)
                    if (detailHtml == null)
                    {
                        byte[] torrent = await HttpClient.Download(t.downloadUri, referer: AppInit.conf.Anidub.host);
                        
                        if (torrent != null && torrent.Length > 0)
                        {
                            string magnet = BencodeTo.Magnet(torrent);
                            string sizeName = BencodeTo.SizeName(torrent);

                            if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
                            {
                                t.magnet = magnet;
                                t.sizeName = sizeName;

                                if (exists)
                                {
                                    updatedCount++;
                                    ParserLog.WriteUpdated("anidub", t, "magnet from downloadUri");
                                }
                                else
                                {
                                    addedCount++;
                                    ParserLog.WriteAdded("anidub", t);
                                }
                                return true;
                            }
                        }
                    }

                    // If download failed, try parsing detail page for download link and magnet
                    // Reuse detailHtml if we already fetched it in the skip check above
                    if (detailHtml == null)
                    {
                        detailHtml = await HttpClient.Get(t.url, encoding: Encoding.UTF8, useproxy: AppInit.conf.Anidub.useproxy);
                    }
                    
                    if (detailHtml != null)
                    {
                        // Extract relased from detail page
                        int relased = ExtractRelased(detailHtml);
                        if (relased > 0)
                            t.relased = relased;

                        // Look for magnet link first
                        var magnetMatch = Regex.Match(detailHtml, "href=\"(magnet:\\?[^\"]+)\"", RegexOptions.IgnoreCase);
                        if (magnetMatch.Success)
                        {
                            t.magnet = magnetMatch.Groups[1].Value;

                            // Try to get size from detail page
                            var sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                            if (!sizeMatch.Success)
                                sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*([^<]+)", RegexOptions.IgnoreCase);
                            string sizeName = sizeMatch.Success ? HttpUtility.HtmlDecode(sizeMatch.Groups[1].Value).Trim() : null;
                            if (sizeMatch.Success)
                                t.sizeName = sizeName;

                            if (exists)
                            {
                                updatedCount++;
                                ParserLog.WriteUpdated("anidub", t, "magnet from detail page");
                            }
                            else
                            {
                                addedCount++;
                                ParserLog.WriteAdded("anidub", t);
                            }
                            return true;
                        }

                        // Look for download.php link
                        var downloadMatch = Regex.Match(detailHtml, "href=\"([^\"]*engine/download\\.php\\?id=[0-9]+)\"", RegexOptions.IgnoreCase);
                        if (downloadMatch.Success)
                        {
                            string downloadUrl = downloadMatch.Groups[1].Value;
                            if (!downloadUrl.StartsWith("http"))
                                downloadUrl = $"{AppInit.conf.Anidub.host}/{downloadUrl.TrimStart('/')}";

                            // Try to download the torrent file
                            byte[] torrentFile = await HttpClient.Download(downloadUrl, referer: t.url, useproxy: AppInit.conf.Anidub.useproxy);
                            if (torrentFile != null && torrentFile.Length > 0)
                            {
                                string magnet = BencodeTo.Magnet(torrentFile);
                                string sizeName = BencodeTo.SizeName(torrentFile);

                            if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
                            {
                                t.magnet = magnet;
                                t.sizeName = sizeName;

                                // Extract relased from detail page if not already set
                                if (t.relased == 0)
                                {
                                    int relased = ExtractRelased(detailHtml);
                                    if (relased > 0)
                                        t.relased = relased;
                                }

                                if (exists)
                                {
                                    updatedCount++;
                                    ParserLog.WriteUpdated("anidub", t, "magnet from torrent file");
                                }
                                else
                                {
                                    addedCount++;
                                    ParserLog.WriteAdded("anidub", t);
                                }
                                return true;
                            }
                            }

                            // If torrent download failed, try to get size from HTML
                            var sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                            if (!sizeMatch.Success)
                                sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*([^<]+)", RegexOptions.IgnoreCase);
                            if (sizeMatch.Success)
                                t.sizeName = HttpUtility.HtmlDecode(sizeMatch.Groups[1].Value).Trim();
                        }
                    }

                    failedCount++;
                    ParserLog.WriteFailed("anidub", t, "could not get magnet or size");
                    return false;
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write("anidub", $"Page {page} completed",
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
