using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.Knaben
{
    public class KnabenSyncService
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

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        public async Task<string> ParseAsync(
            int from = 0,
            int size = 300,
            int pages = 1,
            string query = null,
            int hours = 0,
            string orderBy = "date",
            string categories = null,
            CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                if (!EnsureConfig())
                    return "config missing";

                int s = Math.Min(MaxSize, Math.Max(1, size));
                int p = Math.Max(1, Math.Min(MaxPages, pages));
                int[] cats = ParseCategories(categories);

                return await ParseCore(from, s, p, query?.Trim(), hours, orderBy, cats, cancellationToken);
            });
        }

        static bool EnsureConfig()
        {
            if (AppInit.conf?.Knaben != null) return true;
            ParserLog.Write(TrackerName, "Config missing — add Knaben to init.yaml");
            return false;
        }

        static int[] ParseCategories(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return DefaultCategories;
            var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var parsed = parts
                .Select(p => int.TryParse(p.Trim(), out int id) ? id : (int?)null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray();
            return parsed.Length > 0 ? parsed : DefaultCategories;
        }

        async Task<string> ParseCore(int from, int size, int pages, string query, int hours, string orderBy, int[] categories, CancellationToken cancellationToken)
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
                    cancellationToken.ThrowIfCancellationRequested();
                    int offset = from + page * size;
                    var batch = await FetchTorrentsFromApi(offset, size, secondsSince, query, orderBy, categories, cancellationToken);
                    if (batch == null || batch.Count == 0) break;
                    all.AddRange(batch);
                    totalFetched += batch.Count;
                    if (batch.Count < size) break;
                    if (page < pages - 1) await Task.Delay(ApiDelayMs, cancellationToken);
                }

                if (all.Count > 0)
                {
                    (added, updated, skipped, failed) = await SaveTorrents(all, cancellationToken);
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
                if (ex is OutOfMemoryException) throw;
                ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                return $"error: {ex.Message}";
            }
        }

        async Task<KnabenApiResponse> ApiRequestAsync(KnabenApiRequest req, CancellationToken cancellationToken)
        {
            if (AppInit.conf?.Knaben == null) return null;

            var json = JsonConvert.SerializeObject(req, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            using var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");

            cancellationToken.ThrowIfCancellationRequested();
            string response = await HttpClient.Post(ApiUrl, content, timeoutSeconds: 15, useproxy: AppInit.conf.Knaben.useproxy);
            if (string.IsNullOrWhiteSpace(response))
            {
                ParserLog.Write(TrackerName, "API empty response");
                return null;
            }

            return JsonConvert.DeserializeObject<KnabenApiResponse>(response);
        }

        async Task<List<TorrentDetails>> FetchTorrentsFromApi(int from, int size, int? secondsSince, string query, string orderBy, int[] categories, CancellationToken cancellationToken)
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

            await Task.Delay(ApiDelayMs, cancellationToken);

            var resp = await ApiRequestAsync(req, cancellationToken);
            if (resp?.Hits == null || resp.Hits.Count == 0) return new List<TorrentDetails>();

            return resp.Hits.Select(KnabenParser.MapToTorrentDetails).Where(t => t != null).ToList();
        }

        async Task<(int added, int updated, int skipped, int failed)> SaveTorrents(List<TorrentDetails> torrents, CancellationToken cancellationToken)
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

                await Task.Delay(ApiDelayMs, cancellationToken);
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
    }
}
