using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Engine;
using JacRed.Engine.CORE;
using JacRed.Models.Details;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/animelayer/[action]")]
    public class AnimeLayerController : BaseController
    {
        #region TakeLogin
        /// <summary>
        /// Retrieves cached cookie for Animelayer authentication.
        /// </summary>
        /// <returns>Cached cookie string if available, null otherwise.</returns>
        private string Cookie()
        {
            if (memoryCache.TryGetValue("animelayer:cookie", out string cookie))
                return cookie;

            return null;
        }

        /// <summary>
        /// Attempts to login to Animelayer using configured credentials and cache the authentication cookie.
        /// </summary>
        /// <returns>True if login was successful and cookie was cached, false otherwise.</returns>
        async public Task<bool> TakeLogin()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.u) ||
                    string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.p))
                {
                    ParserLog.Write("animelayer", "TakeLogin failed", new Dictionary<string, object>
                    {
                        { "reason", "credentials not configured" }
                    });
                    return false;
                }

                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                    var postParams = new Dictionary<string, string>
                    {
                        { "username", AppInit.conf.Animelayer.login.u },
                        { "password", AppInit.conf.Animelayer.login.p },
                        { "login", "1" }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        string loginUrl = $"{AppInit.conf.Animelayer.host}/login/";
                        using (var response = await client.PostAsync(loginUrl, postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                            {
                                string layerHash = null, layerId = null, phpsessid = null;
                                foreach (string line in cookies.Where(line => !string.IsNullOrWhiteSpace(line)))
                                {
                                    if (line.Contains("layer_hash="))
                                        layerHash = new Regex("layer_hash=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("layer_id="))
                                        layerId = new Regex("layer_id=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("PHPSESSID="))
                                        phpsessid = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(layerHash) && !string.IsNullOrWhiteSpace(layerId))
                                {
                                    string cookieValue = $"layer_hash={layerHash};layer_id={layerId}";
                                    if (!string.IsNullOrWhiteSpace(phpsessid))
                                        cookieValue += $";PHPSESSID={phpsessid}";

                                    memoryCache.Set("animelayer:cookie", cookieValue, DateTime.Now.AddDays(1));

                                    ParserLog.Write("animelayer", "TakeLogin successful", new Dictionary<string, object>
                                    {
                                        { "user", AppInit.conf.Animelayer.login.u }
                                    });
                                    return true;
                                }
                            }
                        }
                    }
                }

                ParserLog.Write("animelayer", "TakeLogin failed", new Dictionary<string, object>
                {
                    { "reason", "no valid cookies received" }
                });
            }
            catch (Exception ex)
            {
                ParserLog.Write("animelayer", "TakeLogin error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
            }

            return false;
        }
        #endregion

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
        /// Parses torrent releases from Animelayer website pages.
        /// </summary>
        /// <param name="parseFrom">The starting page number to parse from. If 0 or less, defaults to page 1.</param>
        /// <param name="parseTo">The ending page number to parse to. If 0 or less, defaults to the same value as parseFrom.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a status string:
        /// - "work" if parsing is already in progress
        /// - "work_login" if cookie needs to be refreshed
        /// - "canceled" if the operation was canceled
        /// - "ok" if parsing completed successfully
        /// </returns>
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            #region Authorization
            // Check if we need to get a cookie from login
            string cookie = Cookie();

            if (string.IsNullOrWhiteSpace(cookie))
            {
                // Try to use configured cookie first
                if (!string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.cookie))
                {
                    cookie = AppInit.conf.Animelayer.cookie;
                }
                // If no static cookie and we have credentials, try login
                else if (!string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.u) &&
                         !string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.p))
                {
                    if (await TakeLogin())
                    {
                        cookie = Cookie();
                    }
                    else
                    {
                        ParserLog.Write("animelayer", "Authorization failed", new Dictionary<string, object>
                        {
                            { "reason", "login failed" }
                        });
                        return "work_login";
                    }
                }
                else
                {
                    ParserLog.Write("animelayer", "Authorization failed", new Dictionary<string, object>
                    {
                        { "reason", "no cookie or credentials provided" }
                    });
                    return "Failed to authorize, please provide either cookie or credentials";
                }
            }
            #endregion

            if (!TryStartParse())
                return "work";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = AppInit.conf.Animelayer.host;

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

                ParserLog.Write("animelayer", $"Starting parse", new Dictionary<string, object>
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
                        await Task.Delay(AppInit.conf.Animelayer.parseDelay);

                    if (page > 1)
                    {
                        ParserLog.Write("animelayer", $"Parsing page", new Dictionary<string, object>
                        {
                            { "page", page },
                            { "url", $"{baseUrl}/torrents/anime/?page={page}" }
                        });
                    }

                    (int parsed, int added, int updated, int skipped, int failed) = await parsePage(page, cookie);
                    totalParsed += parsed;
                    totalAdded += added;
                    totalUpdated += updated;
                    totalSkipped += skipped;
                    totalFailed += failed;
                }

                ParserLog.Write("animelayer", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
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
                ParserLog.Write("animelayer", $"Canceled", new Dictionary<string, object>
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

                ParserLog.Write("animelayer", $"Error", new Dictionary<string, object>
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
        /// Parses a single page of torrent releases from the Animelayer website.
        /// </summary>
        /// <param name="page">The page number to parse.</param>
        /// <param name="cookie">The authentication cookie to use for the request.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a tuple with parsing statistics:
        /// - parsed: Total number of torrent releases found and processed
        /// - added: Number of new torrent releases added to the database
        /// - updated: Number of existing torrent releases that were updated
        /// - skipped: Number of torrent releases skipped (no changes detected)
        /// - failed: Number of torrent releases that failed to process
        /// </returns>
        async Task<(int parsed, int added, int updated, int skipped, int failed)> parsePage(int page, string cookie)
        {
            string url = $"{AppInit.conf.Animelayer.host}/torrents/anime/" + (page > 1 ? $"?page={page}" : "");
            string html = await HttpClient.Get(url, cookie: cookie, useproxy: AppInit.conf.Animelayer.useproxy, httpversion: 2);

            if (html == null || !html.Contains("id=\"wrapper\""))
            {
                ParserLog.Write("animelayer", $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", url },
                    { "reason", html == null ? "null response" : "invalid content" }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = new List<TorrentDetails>();
            foreach (string row in tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", ""))).Split("class=\"torrent-item torrent-item-medium panel\"").Skip(1))
            {

                #region Local method - Match
                string Match(string pattern, int index = 1)
                {
                    string res = new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim();
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Creation date
                DateTime createTime = default;

                // Match Russian text: "Добавл" (Added) or "Обновл" (Updated)
                if (Regex.IsMatch(row, "(Добавл|Обновл)[^<]+</span>[0-9]+ [^ ]+ [0-9]{4}"))
                {
                    createTime = tParse.ParseCreateTime(Match(">(Добавл|Обновл)[^<]+</span>([0-9]+ [^ ]+ [0-9]{4})", 2), "dd.MM.yyyy");
                }
                else
                {
                    string date = Match("(Добавл|Обновл)[^<]+</span>([^\n]+) в", 2);
                    if (string.IsNullOrWhiteSpace(date))
                        continue;

                    createTime = tParse.ParseCreateTime($"{date} {DateTime.Today.Year}", "dd.MM.yyyy");
                }

                if (createTime == default)
                {
                    if (page != 1)
                        continue;

                    createTime = DateTime.UtcNow;
                }
                #endregion

                #region Release data
                var gurl = Regex.Match(row, "<a href=\"/(torrent/[a-z0-9]+)/?\">([^<]+)</a>").Groups;

                string urlPath = gurl[1].Value;
                string title = gurl[2].Value;

                string _sid = Match("class=\"icon s-icons-upload\"></i>([0-9]+)");
                string _pir = Match("class=\"icon s-icons-download\"></i>([0-9]+)");

                if (string.IsNullOrWhiteSpace(urlPath) || string.IsNullOrWhiteSpace(title))
                    continue;

                // Match Russian text: "Разрешение" (Resolution)
                if (Regex.IsMatch(row, "Разрешение: ?</strong>1920x1080"))
                    title += " [1080p]";
                else if (Regex.IsMatch(row, "Разрешение: ?</strong>1280x720"))
                    title += " [720p]";

                string fullUrl = $"{AppInit.conf.Animelayer.host}/{urlPath}/";
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                // Example format: "Original Name (2021) / Russian Name [TV] (1-7)"
                var g = Regex.Match(title, "([^/\\[\\(]+)\\([0-9]{4}\\)[^/]+/([^/\\[\\(]+)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[2].Value.Trim();
                    originalname = g[1].Value.Trim();
                }
                else
                {
                    // Example format: "Original Name / Russian Name (1—6)"
                    g = Regex.Match(title, "^([^/\\[\\(]+)/([^/\\[\\(]+)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                    {
                        name = g[2].Value.Trim();
                        originalname = g[1].Value.Trim();
                    }
                }
                #endregion

                // Release year (matches Russian text: "Год выхода")
                if (!int.TryParse(Match("Год выхода: ?</strong>([0-9]{4})"), out int relased) || relased == 0)
                    continue;

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "animelayer",
                        types = ["anime"],
                        url = fullUrl,
                        title = title,
                        sid = sid,
                        pir = pir,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
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

                    // Check if already exists with same title (skip if unchanged)
                    if (exists && _tcache.title == t.title)
                    {
                        skippedCount++;
                        // Use existing cache data for logging
                        ParserLog.WriteSkipped("animelayer", _tcache, "no changes");
                        return true;
                    }

                    // Try to download torrent file
                    byte[] torrent = await HttpClient.Download($"{t.url}download/", cookie: cookie, useproxy: AppInit.conf.Animelayer.useproxy);
                    string magnet = BencodeTo.Magnet(torrent);
                    string sizeName = BencodeTo.SizeName(torrent);

                    if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
                    {
                        t.magnet = magnet;
                        t.sizeName = sizeName;

                        if (exists)
                        {
                            updatedCount++;
                            ParserLog.WriteUpdated("animelayer", t, "magnet from download");
                        }
                        else
                        {
                            addedCount++;
                            ParserLog.WriteAdded("animelayer", t);
                        }
                        return true;
                    }

                    failedCount++;
                    ParserLog.WriteFailed("animelayer", t, "could not get magnet or size");
                    return false;
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write("animelayer", $"Page {page} completed",
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
