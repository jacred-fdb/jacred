using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using JacRed.Engine.CORE;
using JacRed.Engine;
using JacRed.Models.Details;
using JacRed.Models.tParse;

namespace JacRed.Controllers.CRON
{
    /// <summary>
    /// Knaben API v1 — TV + Movies. Aggregates torrents from multiple trackers.
    /// https://api.knaben.org/v1
    /// </summary>
    [Route("/cron/knaben/[action]")]
    public class KnabenController : BaseController
    {
        const string TrackerName = "knaben";
        const int MinApiDelayMs = 500;
        const int MaxSize = 300;
        const int MaxPages = 10;

        static readonly int[] DefaultCategories =
        {
            2000000, 2001000, 2002000, 2003000, 2004000, 2005000, 2006000, 2007000, 2008000,
            3000000, 3001000, 3002000, 3003000, 3004000, 3005000, 3006000, 3007000, 3008000
        };

        static string ApiUrl => $"{AppInit.conf.Knaben.host.TrimEnd('/')}/v1";
        static int ApiDelayMs => Math.Max(MinApiDelayMs, AppInit.conf.Knaben.parseDelay);

        static volatile bool _workParse;
        static readonly object _workLock = new object();

        #region Parse

        static bool TryStartParse()
        {
            lock (_workLock)
            {
                if (_workParse) return false;
                _workParse = true;
                return true;
            }
        }

        static void EndParse() { lock (_workLock) { _workParse = false; } }

        static bool EnsureConfig()
        {
            if (AppInit.conf?.Knaben != null) return true;
            ParserLog.Write(TrackerName, "Config missing — add Knaben to init.yaml");
            return false;
        }

        /// <summary>
        /// Parse Knaben API. Params: from, size, pages, query, hours, orderBy, categories.
        /// Examples: /cron/knaben/parse | /cron/knaben/parse?pages=3 | /cron/knaben/parse?query=Breaking+Bad
        /// </summary>
        async public Task<string> Parse(
            int from = 0,
            int size = 300,
            int pages = 1,
            string query = null,
            int hours = 0,
            string orderBy = "date",
            string categories = null)
        {
            if (!TryStartParse()) { ParserLog.Write(TrackerName, "Skipped — already running"); return "work"; }
            try
            {
                if (!EnsureConfig()) return "config missing";

                int s = Math.Min(MaxSize, Math.Max(1, size));
                int p = Math.Max(1, Math.Min(MaxPages, pages));
                int[] cats = ParseCategories(categories);

                return await ParseCore(from, s, p, query?.Trim(), hours, orderBy, cats);
            }
            finally { EndParse(); }
        }

        /// <summary>Parse categories from comma/semicolon/space-separated string. Returns DefaultCategories if empty or invalid.</summary>
        static int[] ParseCategories(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return DefaultCategories;
            var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var parsed = parts
                .Select(p => int.TryParse(p.Trim(), out int id) ? id : (int?)null)
                .Where(x => x.HasValue)
                .Select(x => x.Value)
                .ToArray();
            return parsed.Length > 0 ? parsed : DefaultCategories;
        }

        async Task<string> ParseCore(int from, int size, int pages, string query, int hours, string orderBy, int[] categories)
        {
            var sw = Stopwatch.StartNew();
            int totalFetched = 0, added = 0, updated = 0, skipped = 0, failed = 0;

            try
            {
                var opts = new Dictionary<string, object> { { "from", from }, { "size", size }, { "pages", pages } };
                if (!string.IsNullOrEmpty(query)) opts["query"] = query;
                if (hours > 0) opts["hours"] = hours;
                opts["orderBy"] = orderBy;
                ParserLog.Write(TrackerName, "Starting parse", opts);

                var all = new List<TorrentDetails>();
                int? secondsSince = hours > 0 ? hours * 3600 : (int?)null;

                for (int page = 0; page < pages; page++)
                {
                    int offset = from + page * size;
                    var batch = await FetchTorrentsFromApi(offset, size, secondsSince, query, orderBy, categories);
                    if (batch == null || batch.Count == 0) break;
                    all.AddRange(batch);
                    totalFetched += batch.Count;
                    if (batch.Count < size) break;
                    if (page < pages - 1) await Task.Delay(ApiDelayMs);
                }

                if (all.Count > 0)
                {
                    (added, updated, skipped, failed) = await SaveTorrents(all);
                }

                ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                    new Dictionary<string, object> { { "fetched", totalFetched }, { "added", added }, { "updated", updated }, { "skipped", skipped }, { "failed", failed } });
                return $"fetched={totalFetched} +{added} ~{updated} ={skipped} failed={failed}";
            }
            catch (OperationCanceledException oce)
            {
                ParserLog.Write(TrackerName, "Canceled", new Dictionary<string, object> { { "message", oce.Message } });
                return "canceled";
            }
            catch (Exception ex)
            {
                ParserLog.Write(TrackerName, "Error", new Dictionary<string, object> { { "message", ex.Message } });
                return $"error: {ex.Message}";
            }
        }

        #endregion

        #region API

        async Task<KnabenApiResponse> ApiRequestAsync(KnabenApiRequest req)
        {
            if (AppInit.conf?.Knaben == null) return null;

            var json = JsonConvert.SerializeObject(req, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            using var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");

            string response = await HttpClient.Post(ApiUrl, content, timeoutSeconds: 15, useproxy: AppInit.conf.Knaben.useproxy);
            if (string.IsNullOrWhiteSpace(response))
            {
                ParserLog.Write(TrackerName, "API empty response");
                return null;
            }

            return JsonConvert.DeserializeObject<KnabenApiResponse>(response);
        }

        async Task<List<TorrentDetails>> FetchTorrentsFromApi(int from, int size, int? secondsSince, string query, string orderBy, int[] categories)
        {
            var req = new KnabenApiRequest
            {
                Categories = categories,
                OrderBy = orderBy == "seeders" || orderBy == "peers" ? orderBy : "date",
                OrderDirection = "desc",
                From = from,
                Size = size,
                HideUnsafe = true,
                HideXxx = true
            };
            if (!string.IsNullOrWhiteSpace(query)) { req.Query = query; req.SearchField = "title"; }
            if (secondsSince.HasValue) req.SecondsSinceLastSeen = secondsSince.Value;

            await Task.Delay(ApiDelayMs);

            var resp = await ApiRequestAsync(req);
            if (resp?.Hits == null || resp.Hits.Count == 0) return new List<TorrentDetails>();

            var list = new List<TorrentDetails>();
            foreach (var h in resp.Hits)
            {
                var t = MapToTorrentDetails(h);
                if (t != null) list.Add(t);
            }
            return list;
        }

        #endregion

        #region Mapping

        TorrentDetails MapToTorrentDetails(KnabenHit h)
        {
            if (string.IsNullOrWhiteSpace(h.Title)) return null;
            var types = GetTypesFromCategoryId(h.CategoryId);
            if (types == null) return null;

            string url = !string.IsNullOrWhiteSpace(h.Details) ? h.Details : h.Link;
            if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(h.Id))
                url = $"https://knaben.xyz/?id={h.Id}"; // Fallback when API returns only Id
            if (string.IsNullOrWhiteSpace(url)) return null;

            var title = HttpUtility.HtmlDecode(h.Title.Trim());
            var createTime = ParseDate(h.Date) ?? ParseDate(h.LastSeen) ?? DateTime.UtcNow;
            var updateTime = ParseDate(h.LastSeen) ?? createTime;
            var (name, relased) = ParseNameAndYear(title);

            return new TorrentDetails
            {
                trackerName = TrackerName,
                types = types,
                url = url,
                title = title,
                sid = h.Seeders,
                pir = h.Peers,
                sizeName = FormatSize(h.Bytes),
                createTime = createTime,
                updateTime = updateTime,
                magnet = !string.IsNullOrWhiteSpace(h.MagnetUrl) ? h.MagnetUrl : null,
                _sn = string.IsNullOrWhiteSpace(h.MagnetUrl) && !string.IsNullOrWhiteSpace(h.Link) ? h.Link : null,
                name = name,
                originalname = name,
                relased = relased,
                quality = GetQualityFromCategoryId(h.CategoryId)
            };
        }

        static (string, int) ParseNameAndYear(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return (null, 0);
            string name = title.Trim();
            int relased = 0;
            var m = Regex.Match(title, @"[\(\[](\d{4})[\)\]]");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int y))
            {
                relased = y;
                if (m.Index > 0) name = title.Substring(0, m.Index).TrimEnd(' ', '/', '-', '|');
            }
            return (string.IsNullOrWhiteSpace(name) ? title : name, relased);
        }

        static int GetQualityFromCategoryId(int[] ids)
        {
            if (ids == null) return 480;
            foreach (var id in ids)
            {
                if (id == 2003000 || id == 3003000) return 2160;
                if (id == 2001000 || id == 3001000) return 1080;
                if (id == 2002000 || id == 3002000) return 720;
            }
            return 480;
        }

        static string[] GetTypesFromCategoryId(int[] ids)
        {
            if (ids == null || ids.Length == 0) return new[] { "movie", "serial" };
            foreach (var id in ids)
            {
                if (id >= 2000000 && id < 3000000) return new[] { "serial" };
                if (id >= 3000000 && id < 4000000) return new[] { "movie" };
            }
            return new[] { "movie", "serial" };
        }

        static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dt) ? dt : (DateTime?)null;
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1073741824L) return $"{bytes / 1048576.0:F2} Mb";
            if (bytes < 1099511627776L) return $"{bytes / 1073741824.0:F2} GB";
            return $"{bytes / 1099511627776.0:F2} TB";
        }

        #endregion

        #region Save

        /// <summary>Save to FileDB. Key = url. Log by: added (new), updated (sid/pir/magnet changed), skipped (no change), failed (no magnet).</summary>
        async Task<(int added, int updated, int skipped, int failed)> SaveTorrents(List<TorrentDetails> torrents)
        {
            int added = 0, updated = 0, skipped = 0, failed = 0;

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                bool exists = db.TryGetValue(t.url, out TorrentDetails cached);

                if (exists && cached.title == t.title && string.Equals(cached.magnet?.Trim(), t.magnet?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    if (AppInit.TrackerLogEnabled(TrackerName))
                        ParserLog.WriteSkipped(TrackerName, cached, "no changes");
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(t.magnet))
                {
                    if (exists) { updated++; if (AppInit.TrackerLogEnabled(TrackerName)) ParserLog.WriteUpdated(TrackerName, t, "sid/pir/magnet"); }
                    else { added++; if (AppInit.TrackerLogEnabled(TrackerName)) ParserLog.WriteAdded(TrackerName, t); }
                    return true;
                }

                string downloadUrl = t._sn;
                if (string.IsNullOrWhiteSpace(downloadUrl) || !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    failed++;
                    if (AppInit.TrackerLogEnabled(TrackerName))
                        ParserLog.WriteFailed(TrackerName, t, "no magnet, no link");
                    return false;
                }

                await Task.Delay(ApiDelayMs);
                string referer = !string.IsNullOrWhiteSpace(t.url) ? t.url : null;
                byte[] data = await HttpClient.Download(downloadUrl, referer: referer, timeoutSeconds: 15, useproxy: AppInit.conf.Knaben.useproxy);
                string magnet = data != null ? BencodeTo.Magnet(data) : null;

                if (!string.IsNullOrWhiteSpace(magnet))
                {
                    t.magnet = magnet;
                    t._sn = null;
                    if (exists) { updated++; if (AppInit.TrackerLogEnabled(TrackerName)) ParserLog.WriteUpdated(TrackerName, t, "magnet from link"); }
                    else { added++; if (AppInit.TrackerLogEnabled(TrackerName)) ParserLog.WriteAdded(TrackerName, t); }
                    return true;
                }

                failed++;
                if (AppInit.TrackerLogEnabled(TrackerName))
                    ParserLog.WriteFailed(TrackerName, t, "could not get magnet from link");
                return false;
            });

            return (added, updated, skipped, failed);
        }

        #endregion
    }
}
