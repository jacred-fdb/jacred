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
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.Knaben
{
    public class KnabenSyncService
    {
        const string TrackerName = "knaben";
        const int MinApiDelayMs = 500;
        const int MaxSize = 300;
        const int MaxPages = 10;
        const int MaxFromWindow = 10000;
        const string BackfillStatePath = "Data/temp/knaben_backfill.json";

        static readonly int[] DefaultCategories =
        {
            2000000, 2001000, 2002000, 2003000, 2004000, 2005000, 2006000, 2007000, 2008000,
            3000000, 3001000, 3002000, 3003000, 3004000, 3005000, 3006000, 3007000, 3008000
        };

        /// <summary>Leaf TV/Movies subcategories for archive backfill (no parent 2000000 / 3000000).</summary>
        static readonly int[] BackfillCategories =
        {
            2001000, 2002000, 2003000, 2004000, 2005000, 2006000, 2007000, 2008000,
            3001000, 3002000, 3003000, 3004000, 3005000, 3006000, 3007000, 3008000
        };

        static string ApiUrl => $"{AppInit.conf.Knaben.host.TrimEnd('/')}/v1";
        static int ApiDelayMs => Math.Max(MinApiDelayMs, AppInit.conf.Knaben.parseDelay);

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerParseLock _backfillLock = new TrackerParseLock();
        static readonly object _backfillStateLock = new object();

        public async Task<string> ParseAsync(
            int from = 0,
            int size = 300,
            int pages = 1,
            string query = null,
            int hours = 0,
            string orderBy = "date",
            string orderDirection = "desc",
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
                string dir = NormalizeOrderDirection(orderDirection);

                return await ParseCore(from, s, p, query?.Trim(), hours, orderBy, dir, cats, cancellationToken);
            });
        }

        public async Task<string> BackfillAsync(
            int size = 300,
            int pages = 10,
            bool reset = false,
            CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync($"{TrackerName}-backfill", _backfillLock, checkDisabled: false, async () =>
            {
                if (!EnsureConfig())
                    return "config missing";

                int s = Math.Min(MaxSize, Math.Max(1, size));
                int p = Math.Max(1, Math.Min(MaxPages, pages));
                return await BackfillCore(s, p, reset, cancellationToken);
            });
        }

        public string GetBackfillStatus()
        {
            var state = LoadBackfillState(reset: false);
            return FormatBackfillStatus(state);
        }

        static bool EnsureConfig()
        {
            if (AppInit.conf?.Knaben != null) return true;
            ParserLog.Write(TrackerName, "Config missing — add Knaben to init.yaml");
            return false;
        }

        static string NormalizeOrderDirection(string orderDirection)
            => string.Equals(orderDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";

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

        async Task<string> ParseCore(int from, int size, int pages, string query, int hours, string orderBy, string orderDirection, int[] categories, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            int totalFetched = 0, added = 0, updated = 0, skipped = 0, failed = 0;

            try
            {
                if (from >= MaxFromWindow)
                {
                    ParserLog.Write(TrackerName, "from exceeds Knaben window", new Dictionary<string, object> { { "from", from }, { "max", MaxFromWindow } });
                    return $"error: from must be < {MaxFromWindow} (Knaben from+size ≤ {MaxFromWindow})";
                }

                var opts = new Dictionary<string, object> { { "from", from }, { "size", size }, { "pages", pages }, { "orderDirection", orderDirection } };
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
                    if (offset >= MaxFromWindow) break;
                    int pageSize = Math.Min(size, MaxFromWindow - offset);
                    if (pageSize <= 0) break;

                    var batch = await FetchTorrentsFromApi(offset, pageSize, secondsSince, query, orderBy, orderDirection, categories, cancellationToken);
                    if (!batch.IsValid) break;
                    if (batch.Torrents.Count > 0)
                    {
                        all.AddRange(batch.Torrents);
                        totalFetched += batch.Torrents.Count;
                    }
                    if (batch.RawHitCount < pageSize) break;
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

        async Task<string> BackfillCore(int size, int pages, bool reset, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            int callFetched = 0, callAdded = 0, callUpdated = 0, callSkipped = 0, callFailed = 0;

            try
            {
                var state = LoadBackfillState(reset);
                if (state.Finished)
                {
                    ParserLog.Write(TrackerName, "Backfill already finished", new Dictionary<string, object> { { "status", FormatBackfillStatus(state) } });
                    return FormatBackfillStatus(state) + " (finished)";
                }

                ParserLog.Write(TrackerName, "Starting backfill", new Dictionary<string, object>
                {
                    { "size", size }, { "pages", pages }, { "reset", reset },
                    { "cat", state.CategoryId }, { "dir", state.Direction }, { "from", state.From }
                });

                int pagesDone = 0;
                while (pagesDone < pages && !state.Finished)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (state.From >= MaxFromWindow)
                    {
                        AdvanceBackfillPass(state, lastPageIds: null, earlyEnd: false);
                        PersistBackfillState(state);
                        continue;
                    }

                    int pageSize = Math.Min(size, MaxFromWindow - state.From);
                    if (pageSize <= 0)
                    {
                        AdvanceBackfillPass(state, lastPageIds: null, earlyEnd: false);
                        PersistBackfillState(state);
                        continue;
                    }

                    var (batch, outcome, attempts) = await KnabenBackfillPageLogic.FetchWithRetry(
                        ct => FetchTorrentsFromApi(
                            state.From,
                            pageSize,
                            secondsSince: null,
                            query: null,
                            orderBy: "date",
                            orderDirection: state.Direction,
                            categories: new[] { state.CategoryId },
                            ct),
                        pageSize,
                        state.From,
                        (ms, ct) => Task.Delay(ms, ct),
                        cancellationToken,
                        onAttempt: (page, pageOutcome, attempt) =>
                        {
                            ParserLog.Write(TrackerName, "Backfill fetch", PageLogFields(state, page, pageOutcome, attempt));
                        });

                    if (outcome == KnabenPageOutcome.Retryable)
                    {
                        if (batch.Torrents.Count > 0)
                        {
                            var (added, updated, skipped, failed) = await SaveTorrents(batch.Torrents, cancellationToken);
                            callFetched += batch.Torrents.Count;
                            callAdded += added;
                            callUpdated += updated;
                            callSkipped += skipped;
                            callFailed += failed;
                        }

                        ParserLog.Write(TrackerName, "Backfill page retry exhausted, holding checkpoint",
                            PageLogFields(state, batch, outcome, attempts, reason: "retryHold"));
                        break;
                    }

                    if (batch.Torrents.Count > 0)
                    {
                        var (added, updated, skipped, failed) = await SaveTorrents(batch.Torrents, cancellationToken);
                        callFetched += batch.Torrents.Count;
                        callAdded += added;
                        callUpdated += updated;
                        callSkipped += skipped;
                        callFailed += failed;
                        state.TotalFetched += batch.Torrents.Count;
                        state.TotalAdded += added;
                        state.TotalUpdated += updated;
                    }

                    bool earlyEnd = outcome == KnabenPageOutcome.EndOfFeed;
                    bool isAsc = state.Direction == "asc";
                    var ids = batch.Ids ?? new List<string>();

                    if (isAsc && ids.Count > 0)
                        state.AscEdgeIds = ids;

                    if (!isAsc
                        && state.AscEdgeIds != null
                        && state.AscEdgeIds.Count > 0
                        && ids.Any(id => state.AscEdgeIds.Contains(id)))
                    {
                        state.DescSawOverlap = true;
                    }

                    state.From += pageSize;
                    pagesDone++;

                    if (earlyEnd || state.From >= MaxFromWindow)
                    {
                        bool wasAsc = state.Direction == "asc";
                        bool overlap = !wasAsc
                            && (state.DescSawOverlap
                                || (state.AscEdgeIds != null && ids.Any(id => state.AscEdgeIds.Contains(id))));
                        string reason = wasAsc
                            ? (earlyEnd ? "endOfFeed" : "window")
                            : (overlap ? "overlap" : "partial");
                        var passLog = PageLogFields(state, batch, outcome, attempts, reason);
                        AdvanceBackfillPass(state, ids, earlyEnd);
                        ParserLog.Write(TrackerName, "Backfill pass ended", passLog);
                    }

                    PersistBackfillState(state);

                    if (pagesDone < pages && !state.Finished)
                        await Task.Delay(ApiDelayMs, cancellationToken);
                }

                ParserLog.Write(TrackerName, $"Backfill step completed (took {sw.Elapsed.TotalSeconds:F1}s)",
                    new Dictionary<string, object>
                    {
                        { "fetched", callFetched }, { "added", callAdded }, { "updated", callUpdated },
                        { "skipped", callSkipped }, { "failed", callFailed },
                        { "cat", state.CategoryId }, { "dir", state.Direction }, { "from", state.From },
                        { "finished", state.Finished }
                    });

                return $"cat={state.CategoryId} dir={state.Direction} from={state.From} "
                    + $"fetched={callFetched} +{callAdded} ~{callUpdated} ={callSkipped} failed={callFailed} "
                    + FormatBackfillProgress(state)
                    + (state.Finished ? " finished" : "");
            }
            catch (OperationCanceledException oce)
            {
                ParserLog.Write(TrackerName, "Backfill canceled", new Dictionary<string, object> { { "message", oce.Message } });
                return "canceled";
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
                ParserLog.Write(TrackerName, $"Backfill error: {ex.Message}");
                return $"error: {ex.Message}";
            }
        }

        internal static void AdvanceBackfillPass(KnabenBackfillState state, List<string> lastPageIds, bool earlyEnd)
        {
            bool isAsc = state.Direction == "asc";

            if (isAsc)
            {
                if (earlyEnd)
                {
                    SetCategoryStatus(state, state.CategoryId, "complete");
                    MoveToNextBackfillCategory(state);
                }
                else
                {
                    // Hit 10k window — keep AscEdgeIds, switch to desc.
                    state.Direction = "desc";
                    state.From = 0;
                    state.DescSawOverlap = false;
                }
                return;
            }

            // desc pass finished
            bool overlap = state.DescSawOverlap
                || (state.AscEdgeIds != null && lastPageIds != null && lastPageIds.Any(id => state.AscEdgeIds.Contains(id)));
            SetCategoryStatus(state, state.CategoryId, overlap ? "complete" : "partial");
            MoveToNextBackfillCategory(state);
        }

        static void MoveToNextBackfillCategory(KnabenBackfillState state)
        {
            state.CategoryIndex++;
            state.AscEdgeIds = new List<string>();
            state.DescSawOverlap = false;
            state.Direction = "asc";
            state.From = 0;

            if (state.CategoryIndex >= BackfillCategories.Length)
            {
                state.Finished = true;
                state.CategoryId = 0;
                return;
            }

            state.CategoryId = BackfillCategories[state.CategoryIndex];
            EnsureCategoryPending(state, state.CategoryId);
        }

        static void EnsureCategoryPending(KnabenBackfillState state, int categoryId)
        {
            string key = categoryId.ToString();
            if (!state.CategoryStatus.ContainsKey(key))
                state.CategoryStatus[key] = "pending";
        }

        static void SetCategoryStatus(KnabenBackfillState state, int categoryId, string status)
        {
            state.CategoryStatus[categoryId.ToString()] = status;
        }

        static KnabenBackfillState CreateFreshBackfillState()
        {
            var state = new KnabenBackfillState
            {
                CategoryIndex = 0,
                CategoryId = BackfillCategories[0],
                Direction = "asc",
                From = 0,
                AscEdgeIds = new List<string>(),
                CategoryStatus = new Dictionary<string, string>(),
                Finished = false,
                UpdatedAt = DateTime.UtcNow
            };
            foreach (int cat in BackfillCategories)
                state.CategoryStatus[cat.ToString()] = "pending";
            return state;
        }

        static KnabenBackfillState LoadBackfillState(bool reset)
        {
            lock (_backfillStateLock)
            {
                if (reset || !IO.File.Exists(BackfillStatePath))
                {
                    var fresh = CreateFreshBackfillState();
                    TryWriteBackfillState(fresh);
                    return fresh;
                }

                try
                {
                    var state = JsonConvert.DeserializeObject<KnabenBackfillState>(IO.File.ReadAllText(BackfillStatePath));
                    if (state == null)
                        return CreateFreshBackfillState();

                    if (state.CategoryStatus == null)
                        state.CategoryStatus = new Dictionary<string, string>();
                    if (state.AscEdgeIds == null)
                        state.AscEdgeIds = new List<string>();
                    if (string.IsNullOrWhiteSpace(state.Direction))
                        state.Direction = "asc";
                    state.Direction = NormalizeOrderDirection(state.Direction);

                    if (!state.Finished && (state.CategoryId == 0 || !BackfillCategories.Contains(state.CategoryId)))
                    {
                        if (state.CategoryIndex >= 0 && state.CategoryIndex < BackfillCategories.Length)
                            state.CategoryId = BackfillCategories[state.CategoryIndex];
                        else
                        {
                            var fresh = CreateFreshBackfillState();
                            TryWriteBackfillState(fresh);
                            return fresh;
                        }
                    }

                    return state;
                }
                catch
                {
                    var fresh = CreateFreshBackfillState();
                    TryWriteBackfillState(fresh);
                    return fresh;
                }
            }
        }

        static void PersistBackfillState(KnabenBackfillState state)
        {
            lock (_backfillStateLock)
            {
                state.UpdatedAt = DateTime.UtcNow;
                TryWriteBackfillState(state);
            }
        }

        static void TryWriteBackfillState(KnabenBackfillState state)
        {
            try
            {
                string dir = IO.Path.GetDirectoryName(BackfillStatePath);
                if (!string.IsNullOrEmpty(dir) && !IO.Directory.Exists(dir))
                    IO.Directory.CreateDirectory(dir);
                IO.File.WriteAllText(BackfillStatePath, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch { }
        }

        static string FormatBackfillProgress(KnabenBackfillState state)
        {
            int done = state.CategoryStatus?.Count(kv => kv.Value == "complete" || kv.Value == "partial") ?? 0;
            int partial = state.CategoryStatus?.Count(kv => kv.Value == "partial") ?? 0;
            return $"progress={done}/{BackfillCategories.Length}" + (partial > 0 ? $" partial={partial}" : "");
        }

        static string FormatBackfillStatus(KnabenBackfillState state)
        {
            if (state == null) return "no state";
            return $"finished={state.Finished} cat={state.CategoryId} dir={state.Direction} from={state.From} "
                + $"totalFetched={state.TotalFetched} +{state.TotalAdded} ~{state.TotalUpdated} "
                + FormatBackfillProgress(state)
                + $" updatedAt={state.UpdatedAt:O}";
        }

        static Dictionary<string, object> PageLogFields(
            KnabenBackfillState state,
            KnabenFetchPage page,
            KnabenPageOutcome outcome,
            int attempts,
            string reason = null)
        {
            var fields = new Dictionary<string, object>
            {
                { "cat", state.CategoryId },
                { "dir", state.Direction },
                { "from", state.From },
                { "rawHits", page?.RawHitCount ?? 0 },
                { "mappedTorrents", page?.Torrents?.Count ?? 0 },
                { "total.value", page?.TotalValue },
                { "total.relation", page?.TotalRelation },
                { "outcome", outcome.ToString() },
                { "attempts", attempts },
                { "valid", page?.IsValid ?? false }
            };
            if (!string.IsNullOrEmpty(reason))
                fields["reason"] = reason;
            return fields;
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

        async Task<KnabenFetchPage> FetchTorrentsFromApi(
            int from,
            int size,
            int? secondsSince,
            string query,
            string orderBy,
            string orderDirection,
            int[] categories,
            CancellationToken cancellationToken)
        {
            if (from >= MaxFromWindow || size <= 0)
                return KnabenFetchPage.Invalid();

            int clampedSize = Math.Min(size, MaxFromWindow - from);
            if (clampedSize <= 0)
                return KnabenFetchPage.Invalid();

            var req = new KnabenApiRequest
            {
                Categories = categories,
                OrderBy = orderBy == "seeders" || orderBy == "peers" ? orderBy : "date",
                OrderDirection = NormalizeOrderDirection(orderDirection),
                From = from,
                Size = clampedSize,
                HideUnsafe = true,
                HideXxx = true
            };
            if (!string.IsNullOrWhiteSpace(query)) { req.Query = query; req.SearchField = "title"; }
            if (secondsSince.HasValue) req.SecondsSinceLastSeen = secondsSince.Value;

            await Task.Delay(ApiDelayMs, cancellationToken);

            var resp = await ApiRequestAsync(req, cancellationToken);
            return KnabenFetchPage.FromResponse(resp);
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
