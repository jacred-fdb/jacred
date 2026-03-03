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

        /// <summary>
        /// Parses torrent releases from LE-Production.TV (anime, serials, dorama, films, cartoons).
        /// </summary>
        [HttpGet]
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            if (!TryStartParse())
                return "work";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = (AppInit.conf.LeProduction.host ?? "").TrimEnd('/');

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
            // Page 1 = base/, Page 2+ = base/page/N/
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
            string decoded = tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", " ")));

            foreach (string block in decoded.Split("class=\"short-item\"").Skip(1))
            {
                string Match(string pattern, int groupIndex = 1)
                {
                    var m = Regex.Match(block, pattern, RegexOptions.IgnoreCase);
                    string res = m.Success ? m.Groups[groupIndex].Value.Trim() : "";
                    return Regex.Replace(res, @"[\n\r\t\s]+", " ").Trim();
                }

                // Release URL: short-img href or h3 a href
                var linkMatch = Regex.Match(block, @"<a\s+class=""short-img""\s+href=""(https?://[^""]+)""", RegexOptions.IgnoreCase);
                if (!linkMatch.Success)
                    linkMatch = Regex.Match(block, @"<h3>\s*<a\s+href=""(https?://[^""]+)""[^>]*>", RegexOptions.IgnoreCase);
                string releaseUrl = linkMatch.Success ? linkMatch.Groups[1].Value.Trim() : null;
                if (string.IsNullOrWhiteSpace(releaseUrl))
                    continue;

                // Normalize to base host if relative
                if (releaseUrl.StartsWith("/"))
                    releaseUrl = baseUrl.TrimEnd('/') + releaseUrl;

                string title = Match(@"<h3>\s*<a[^>]+>([^<]+)</a>\s*</h3>");
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                // Category from short-cat link
                string category = "serial";
                var catMatch = Regex.Match(block, @"short-cat""[^>]*>\s*<a\s+href=""[^""]*/(anime|serial|dorama|film|fulcartoon|cartoon)/?", RegexOptions.IgnoreCase);
                if (catMatch.Success)
                    category = catMatch.Groups[1].Value.ToLowerInvariant();

                var releaseTorrents = await parseReleasePage(baseUrl, releaseUrl, title, category);
                if (releaseTorrents != null)
                    torrents.AddRange(releaseTorrents);
            }

            int parsedCount = torrents.Count;
            int addedCount = 0, updatedCount = 0, skippedCount = 0, failedCount = 0;

            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, (t, db) =>
                {
                    bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);
                    if (exists && string.Equals(_tcache.magnet?.Trim(), t.magnet?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        skippedCount++;
                        ParserLog.WriteSkipped("leproduction", _tcache, "no changes");
                        return Task.FromResult(false);
                    }
                    if (exists)
                    {
                        updatedCount++;
                        ParserLog.WriteUpdated("leproduction", t, "magnet changed or updated");
                    }
                    else
                    {
                        addedCount++;
                        ParserLog.WriteAdded("leproduction", t);
                    }
                    return Task.FromResult(true);
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write("leproduction", $"Page {page} completed", new Dictionary<string, object>
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

        /// <summary>
        /// Parses a single release page: extracts name, originalname, year, then each torrent block (magnet, size, seeders, date).
        /// </summary>
        async Task<List<TorrentDetails>> parseReleasePage(string baseUrl, string releaseUrl, string listTitle, string category)
        {
            string html = await HttpClient.Get(releaseUrl, useproxy: AppInit.conf.LeProduction.useproxy);
            if (html == null)
                return null;

            string decoded = tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", " ")));

            string Match(string text, string pattern, int groupIndex = 1)
            {
                var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                string res = m.Success ? m.Groups[groupIndex].Value.Trim() : "";
                return Regex.Replace(res, @"[\n\r\t\s]+", " ").Trim();
            }

            // Мета с страницы релиза
            string name = Match(decoded, @"info-label""[^>]*>Русское название:</div>\s*<div[^>]*>([^<]+)");
            string originalname = Match(decoded, @"info-label""[^>]*>Оригинальное название:</div>\s*<div[^>]*>([^<]+)");
            int relased = 0;
            var yearMatch = Regex.Match(decoded, @"Год выпуска:[^<]*</div>[^<]*<div[^>]*>[^<]*<a[^>]*>([0-9]{4})</a>", RegexOptions.IgnoreCase);
            if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int y))
                relased = y;

            if (string.IsNullOrWhiteSpace(name))
                name = Regex.Split(listTitle, @"[\[\/\(|]", RegexOptions.IgnoreCase)[0].Trim();
            if (string.IsNullOrWhiteSpace(originalname) && listTitle.Contains(" / "))
            {
                var parts = Regex.Split(listTitle, @"\s*/\s*");
                if (parts.Length >= 2)
                {
                    // Убираем серии/сезоны в скобках для стабильного ключа бакета (name:originalname)
                    originalname = Regex.Replace(parts[1].Trim(), @"\s*\[[^\]]*\]\s*$", "").Trim();
                }
            }

            string[] types = GetTypesForCategory(category);

            var list = new List<TorrentDetails>();

            // Блоки торрентов: <div id="torrent_XXXX_info" class="torrent-le"> ... </div>
            foreach (string torrentBlock in decoded.Split(new[] { "class=\"torrent-le\"" }, StringSplitOptions.None).Skip(1))
            {
                // Id торрента: из ссылки do=download&id=XXX или из id="torrent_XXX_info" в начале блока (предыдущий кусок)
                var idMatch = Regex.Match(torrentBlock, @"do=download&amp;id=(\d+)|do=download\?id=(\d+)", RegexOptions.IgnoreCase);
                string torrentId = idMatch.Success ? (idMatch.Groups[1].Value ?? idMatch.Groups[2].Value) : null;
                if (string.IsNullOrWhiteSpace(torrentId))
                    continue;

                string magnet = Match(torrentBlock, @"href=""(magnet:\?[^""]+)""", 1);
                if (string.IsNullOrWhiteSpace(magnet))
                    continue;

                string sizeRaw = Match(torrentBlock, @"Размер:\s*<span[^>]*>([^<]+)</span>");
                string sizeName = NormalizeSize(sizeRaw);

                string sidStr = Match(torrentBlock, @"li_distribute_m-le""[^>]*>(\d+)");
                string pirStr = Match(torrentBlock, @"li_swing_m-le""[^>]*>(\d+)");
                int.TryParse(sidStr, out int sid);
                int.TryParse(pirStr, out int pir);

                string dateStr = Match(torrentBlock, @"Загрузил:[^(]*\(([^)]+)\)");
                DateTime createTime = default;
                if (!string.IsNullOrWhiteSpace(dateStr))
                    createTime = tParse.ParseCreateTime(dateStr.Trim(), "d.MM. yyyy HH:mm");
                if (createTime == default)
                    createTime = DateTime.UtcNow;

                string torrentUrl = $"{baseUrl.TrimEnd('/')}/index.php?do=download&id={torrentId}";

                string title = listTitle;
                if (!string.IsNullOrWhiteSpace(sizeName))
                    title = $"{listTitle} / {sizeName}";

                list.Add(new TorrentDetails
                {
                    trackerName = "leproduction",
                    types = types,
                    url = torrentUrl,
                    title = title,
                    sid = sid,
                    pir = pir,
                    createTime = createTime,
                    updateTime = createTime,
                    name = name,
                    originalname = originalname,
                    relased = relased,
                    magnet = magnet,
                    sizeName = sizeName
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
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            raw = raw.Trim();
            if (Regex.IsMatch(raw, @"^\d+[\d.,]*\s*Gb\s*$", RegexOptions.IgnoreCase))
                return raw;
            if (Regex.IsMatch(raw, @"^\d+[\d.,]*\s*Mb\s*$", RegexOptions.IgnoreCase))
                return raw;
            var m = Regex.Match(raw, @"([\d.,]+)\s*(Gb|Mb|Гб|Мб)", RegexOptions.IgnoreCase);
            if (m.Success)
                return $"{m.Groups[1].Value.Trim()} {m.Groups[2].Value.Trim()}";
            return raw;
        }
        #endregion
    }
}
