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

namespace JacRed.Controllers.CRON
{
    /// <summary>Парсер LE-Production.TV: список base/ и base/page/N/, релизы по short-item, торренты по torrent-le.</summary>
    [Route("/cron/leproduction/[action]")]
    public class LeProductionController : BaseController
    {
        #region Parse
        static volatile bool workParse = false;
        private static readonly object workParseLock = new object();

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

        private static void EndParse()
        {
            lock (workParseLock)
            {
                workParse = false;
            }
        }

        [HttpGet]
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            if (!TryStartParse())
                return "work";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = (AppInit.conf.LeProduction.host ?? "").TrimEnd('/');
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    ParserLog.Write("leproduction", "Skipped: LeProduction.host is not set");
                    return "ok";
                }

                int startPage = parseFrom > 0 ? parseFrom : 1;
                int endPage = parseTo > 0 ? parseTo : (parseFrom > 0 ? parseFrom : 1);
                if (startPage > endPage)
                {
                    int temp = startPage;
                    startPage = endPage;
                    endPage = temp;
                }

                ParserLog.Write("leproduction", "Starting parse", new Dictionary<string, object>
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
                        await Task.Delay(AppInit.conf.LeProduction.parseDelay);

                    ParserLog.Write("leproduction", "Parsing page", new Dictionary<string, object>
                    {
                        { "page", page },
                        { "url", page <= 1 ? baseUrl + "/" : $"{baseUrl}/page/{page}/" }
                    });

                    var result = await parsePage(page, baseUrl);
                    totalParsed += result.parsed;
                    totalAdded += result.added;
                    totalUpdated += result.updated;
                    totalSkipped += result.skipped;
                    totalFailed += result.failed;
                }

                ParserLog.Write("leproduction", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
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
                ParserLog.Write("leproduction", "Canceled", new Dictionary<string, object>
                {
                    { "message", oce.Message },
                    { "stackTrace", oce.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
                return "canceled";
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException)
                    throw;
                ParserLog.Write("leproduction", "Error", new Dictionary<string, object>
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
        async Task<(int parsed, int added, int updated, int skipped, int failed)> parsePage(int page, string baseUrl)
        {
            string listUrl = page <= 1 ? $"{baseUrl}/" : $"{baseUrl}/page/{page}/";
            string html = await HttpClient.Get(listUrl, useproxy: AppInit.conf.LeProduction.useproxy);

            if (html == null)
            {
                ParserLog.Write("leproduction", "Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", listUrl },
                    { "reason", "null response" }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = new List<TorrentDetails>();
            int releasesNoTorrents = 0;
            string decoded = tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", " ")));
            int releaseIndex = 0;
            int delayMs = Math.Max(0, Math.Min(500, AppInit.conf.LeProduction.parseDelay));

            foreach (string block in decoded.Split("class=\"short-item\"").Skip(1))
            {
                if (releaseIndex > 0 && delayMs > 0)
                    await Task.Delay(delayMs);
                releaseIndex++;

                string Match(string pattern, int groupIndex = 1)
                {
                    var m = Regex.Match(block, pattern, RegexOptions.IgnoreCase);
                    string res = m.Success ? m.Groups[groupIndex].Value.Trim() : "";
                    return Regex.Replace(res, @"[\n\r\t\s]+", " ").Trim();
                }

                var linkMatch = Regex.Match(block, @"<a\s+class=""short-img""\s+href=""(https?://[^""]+|/[^""]*)""", RegexOptions.IgnoreCase);
                if (!linkMatch.Success)
                    linkMatch = Regex.Match(block, @"<h3>\s*<a\s+href=""(https?://[^""]+|/[^""]*)""[^>]*>", RegexOptions.IgnoreCase);
                string releaseUrl = linkMatch.Success ? linkMatch.Groups[1].Value.Trim() : null;
                if (string.IsNullOrWhiteSpace(releaseUrl))
                    continue;

                if (releaseUrl.StartsWith("/"))
                    releaseUrl = baseUrl.TrimEnd('/') + releaseUrl;

                string title = Match(@"<h3>\s*<a[^>]+>([^<]+)</a>\s*</h3>");
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                string category = "serial";
                var catMatch = Regex.Match(block, @"short-cat""[^>]*>\s*<a\s+href=""[^""]*/(anime|serial|dorama|film|fulcartoon|cartoon)/?", RegexOptions.IgnoreCase);
                if (catMatch.Success)
                    category = catMatch.Groups[1].Value.ToLowerInvariant();

                List<TorrentDetails> releaseTorrents = null;
                try
                {
                    releaseTorrents = await parseReleasePage(baseUrl, releaseUrl, title, category, listUrl);
                }
                catch (Exception ex)
                {
                    ParserLog.Write("leproduction", "Release parse error", new Dictionary<string, object>
                    {
                        { "url", releaseUrl },
                        { "message", ex.Message }
                    });
                }
                if (releaseTorrents != null && releaseTorrents.Count > 0)
                    torrents.AddRange(releaseTorrents);
                else
                    releasesNoTorrents++;
            }

            int parsedCount = torrents.Count;
            int addedCount = 0, updatedCount = 0, skippedCount = 0, failedCount = 0;

            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, async (t, db) =>
                {
                    if (string.IsNullOrWhiteSpace(t.magnet))
                    {
                        var idFromUrl = Regex.Match(t.url ?? "", @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
                        if (idFromUrl.Success)
                        {
                            string downloadUrl = $"{baseUrl.TrimEnd('/')}/index.php?do=download&id={idFromUrl.Groups[1].Value}";
                            string dlHtml = await HttpClient.Get(downloadUrl, referer: baseUrl.TrimEnd('/') + "/", useproxy: AppInit.conf.LeProduction.useproxy);
                            if (!string.IsNullOrWhiteSpace(dlHtml))
                            {
                                var mm = Regex.Match(dlHtml, @"href\s*=\s*""(magnet:\?[^""]+)""", RegexOptions.IgnoreCase);
                                if (!mm.Success) mm = Regex.Match(dlHtml, @"(magnet:\?[^\s""'<>]+)", RegexOptions.IgnoreCase);
                                if (mm.Success)
                                {
                                    string decodedMagnet = HttpUtility.HtmlDecode(mm.Groups[1].Value.Trim());
                                    if (!string.IsNullOrWhiteSpace(decodedMagnet))
                                        t.magnet = decodedMagnet;
                                }
                            }
                        }
                        if (string.IsNullOrWhiteSpace(t.magnet))
                        {
                            failedCount++;
                            ParserLog.WriteFailed("leproduction", t, "no magnet on release or download page");
                            return false;
                        }
                    }

                    bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);
                    if (exists && string.Equals(_tcache.magnet?.Trim(), t.magnet?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        skippedCount++;
                        ParserLog.WriteSkipped("leproduction", _tcache, "no changes");
                        return false;
                    }
                    if (exists)
                    {
                        updatedCount++;
                        ParserLog.WriteUpdated("leproduction", t, "updated");
                    }
                    else
                    {
                        addedCount++;
                        ParserLog.WriteAdded("leproduction", t);
                    }
                    return true;
                });
            }

            if (parsedCount > 0 || releasesNoTorrents > 0)
            {
                var pageData = new Dictionary<string, object>
                {
                    { "parsed", parsedCount },
                    { "added", addedCount },
                    { "updated", updatedCount },
                    { "skipped", skippedCount },
                    { "failed", failedCount }
                };
                if (releasesNoTorrents > 0)
                    pageData["releasesNoTorrents"] = releasesNoTorrents;
                ParserLog.Write("leproduction", $"Page {page} completed", pageData);
            }

            return (parsedCount, addedCount, updatedCount, skippedCount, failedCount);
        }

        async Task<List<TorrentDetails>> parseReleasePage(string baseUrl, string releaseUrl, string listTitle, string category, string referer = null)
        {
            string html = await HttpClient.Get(releaseUrl, referer: referer, useproxy: AppInit.conf.LeProduction.useproxy);
            if (html == null)
            {
                ParserLog.Write("leproduction", "Release page failed", new Dictionary<string, object> { { "url", releaseUrl } });
                return null;
            }

            string decoded = tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", " ")));

            string Match(string text, string pattern, int groupIndex = 1)
            {
                var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                string res = m.Success ? m.Groups[groupIndex].Value.Trim() : "";
                return Regex.Replace(res, @"[\n\r\t\s]+", " ").Trim();
            }

            string name = Match(decoded, @"info-label""[^>]*>Русское название:</div>\s*<div[^>]*>([^<]+)");
            string originalname = Match(decoded, @"info-label""[^>]*>Оригинальное название:</div>\s*<div[^>]*>([^<]+)");
            if (string.IsNullOrWhiteSpace(originalname))
                originalname = Match(decoded, @"info-label""[^>]*>Оригинал:</div>\s*<div[^>]*>([^<]+)");
            int relased = 0;
            var yearMatch = Regex.Match(decoded, @"Год выпуска:[^<]*</div>[^<]*<div[^>]*>[^<]*<a[^>]*>([0-9]{4})</a>", RegexOptions.IgnoreCase);
            if (!yearMatch.Success)
                yearMatch = Regex.Match(decoded, @"Год:[^<]*</div>[^<]*<div[^>]*>[^<]*<a[^>]*>([0-9]{4})</a>", RegexOptions.IgnoreCase);
            if (!yearMatch.Success)
                yearMatch = Regex.Match(decoded, @"Год:[^<]*</div>[^<]*<div[^>]*>([0-9]{4})", RegexOptions.IgnoreCase);
            if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int y))
                relased = y;

            if (string.IsNullOrWhiteSpace(name))
                name = Regex.Split(listTitle, @"[\[\/\(|]", RegexOptions.IgnoreCase)[0].Trim();
            if (string.IsNullOrWhiteSpace(originalname) && listTitle.Contains(" / "))
            {
                var parts = Regex.Split(listTitle, @"\s*/\s*");
                if (parts.Length >= 2)
                    originalname = Regex.Replace(parts[1].Trim(), @"\s*\[[^\]]*\]\s*$", "").Trim();
            }

            string[] types = GetTypesForCategory(category);

            var list = new List<TorrentDetails>();

            foreach (string torrentBlock in decoded.Split(new[] { "class=\"torrent-le\"" }, StringSplitOptions.None).Skip(1))
            {
                var idMatch = Regex.Match(torrentBlock, @"do=download(&amp;|&|\?)id=(\d+)", RegexOptions.IgnoreCase);
                string torrentId = idMatch.Success ? idMatch.Groups[2].Value : null;
                if (string.IsNullOrWhiteSpace(torrentId))
                {
                    var idBlockMatch = Regex.Match(torrentBlock, @"id\s*=\s*""torrent_(\d+)_info""", RegexOptions.IgnoreCase);
                    torrentId = idBlockMatch.Success ? idBlockMatch.Groups[1].Value : null;
                }
                if (string.IsNullOrWhiteSpace(torrentId))
                    continue;

                string magnet = Match(torrentBlock, @"href=""(magnet:\?[^""]+)""", 1);
                if (string.IsNullOrWhiteSpace(magnet))
                {
                    var magnetAlt = Regex.Match(torrentBlock, @"(magnet:\?[^\s""'<>]+)", RegexOptions.IgnoreCase);
                    magnet = magnetAlt.Success ? HttpUtility.HtmlDecode(magnetAlt.Groups[1].Value.Trim()) : null;
                }

                string sizeRaw = Match(torrentBlock, @"Размер:\s*<span[^>]*>([^<]+)</span>");
                string sizeName = NormalizeSize(sizeRaw);

                string sidStr = Match(torrentBlock, @"li_distribute_m-le""[^>]*>(\d+)");
                string pirStr = Match(torrentBlock, @"li_swing_m-le""[^>]*>(\d+)");
                int.TryParse(sidStr, out int sid);
                int.TryParse(pirStr, out int pir);

                string dateStr = Match(torrentBlock, @"Загрузил:[^(]*\(([^)]+)\)");
                DateTime createTime = default;
                if (!string.IsNullOrWhiteSpace(dateStr))
                {
                    dateStr = dateStr.Trim();
                    createTime = tParse.ParseCreateTime(dateStr, "d.MM. yyyy HH:mm");
                    if (createTime == default)
                        createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy HH:mm");
                }
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                DateTime updateTime = DateTime.UtcNow;

                string fileName = Match(torrentBlock, @"info_d1-le""[^>]*>([^<]+)", 1);
                int qualityFromFile = 0;
                int seasonFromFile = 0;
                string videotypeFromFile = null;
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var qMatch = Regex.Match(fileName, @"\b(\d{3,4})p\b", RegexOptions.IgnoreCase);
                    if (qMatch.Success && int.TryParse(qMatch.Groups[1].Value, out int q) && (q == 480 || q == 720 || q == 1080 || q == 2160))
                        qualityFromFile = q;
                    var sMatch = Regex.Match(fileName, @"\b[Ss](\d+)\b", RegexOptions.IgnoreCase);
                    if (sMatch.Success && int.TryParse(sMatch.Groups[1].Value, out int s) && s > 0)
                        seasonFromFile = s;
                    if (Regex.IsMatch(fileName, @"\b(HDR|10-?bit|10\s*bit)\b", RegexOptions.IgnoreCase))
                        videotypeFromFile = "hdr";
                }

                string torrentUrl = releaseUrl.IndexOf('?') >= 0
                    ? $"{releaseUrl}&id={torrentId}"
                    : $"{releaseUrl}?id={torrentId}";

                string title = listTitle;
                if (!string.IsNullOrWhiteSpace(sizeName))
                    title = $"{listTitle} / {sizeName}";
                if (qualityFromFile > 0)
                    title += $" [{qualityFromFile}p]";
                if (seasonFromFile > 0)
                    title += $" S{seasonFromFile}";
                if (videotypeFromFile == "hdr")
                    title += " [HDR]";
                title += " LE-Production";

                double sizeNum = 0;
                if (!string.IsNullOrWhiteSpace(sizeName))
                {
                    var sizeNumMatch = Regex.Match(sizeName, @"([\d.,]+)\s*G[bB]", RegexOptions.IgnoreCase);
                    if (sizeNumMatch.Success && double.TryParse(sizeNumMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double gb))
                        sizeNum = gb;
                    else
                    {
                        sizeNumMatch = Regex.Match(sizeName, @"([\d.,]+)\s*M[bB]", RegexOptions.IgnoreCase);
                        if (sizeNumMatch.Success && double.TryParse(sizeNumMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double mb))
                            sizeNum = mb / 1024.0;
                    }
                }

                list.Add(new TorrentDetails
                {
                    trackerName = "leproduction",
                    types = types,
                    url = torrentUrl,
                    title = title,
                    sid = sid,
                    pir = pir,
                    createTime = createTime,
                    updateTime = updateTime,
                    name = name,
                    originalname = originalname,
                    relased = relased,
                    magnet = magnet,
                    sizeName = sizeName,
                    size = sizeNum
                });
            }

            return list.Count > 0 ? list : null;
        }

        private static string[] GetTypesForCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return new[] { "serial" };
            switch (category.ToLowerInvariant())
            {
                case "anime": return new[] { "anime" };
                case "dorama": return new[] { "dorama" };
                case "film": return new[] { "movie" };
                case "fulcartoon": return new[] { "multfilm" };
                case "cartoon": return new[] { "multserial" };
                default: return new[] { "serial" };
            }
        }

        private static string NormalizeSize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var m = Regex.Match(raw.Trim(), @"([\d.,]+)\s*(Gb|Mb|Гб|Мб)", RegexOptions.IgnoreCase);
            return m.Success ? $"{m.Groups[1].Value.Trim()} {m.Groups[2].Value.Trim()}" : raw.Trim();
        }
        #endregion
    }
}
