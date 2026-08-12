using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.SubsPlease
{
    /// <summary>
    /// SubsPlease sync — public JSON API, 1080p magnets only.
    /// Parse: f=latest pages. ParseShows: schedule-prioritized catalog + f=show (Batches).
    /// </summary>
    public class SubsPleaseSyncService
    {
        const string TrackerName = SubsPleaseParser.TrackerName;
        const string CheckpointPath = "Data/temp/subsplease_shows.json";
        const int DefaultPages = 2;
        const int DefaultShowLimit = 50;
        const int MaxPages = 50;
        const int MaxShowLimit = 200;

        static readonly TrackerParseLock ParseLock = new TrackerParseLock();
        static readonly TrackerParseLock ShowsLock = new TrackerParseLock();
        static readonly object CheckpointFileLock = new object();

        static string Host() => (AppInit.conf.SubsPlease?.rqHost() ?? "").TrimEnd('/');

        static bool UseProxy() => AppInit.conf.SubsPlease?.useproxy == true;

        static int ParseDelayMs() => Math.Max(0, AppInit.conf.SubsPlease?.parseDelay ?? 1000);

        static List<(string name, string val)> JsonHeaders() =>
            new List<(string name, string val)> { ("Accept", "application/json") };

        public async Task<string> ParseAsync(int pages = DefaultPages)
        {
            if (string.IsNullOrEmpty(Host()))
                return TrackerSyncHelpers.DisabledResult;

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, ParseLock, checkDisabled: false, async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    int pageCap = Math.Clamp(pages <= 0 ? DefaultPages : pages, 1, MaxPages);
                    string host = Host();

                    ParserLog.Write(TrackerName, "Starting latest parse", new Dictionary<string, object>
                    {
                        { "pages", pageCap },
                        { "host", host }
                    });

                    int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                    for (int i = 0; i < pageCap; i++)
                    {
                        if (i > 0 && ParseDelayMs() > 0)
                            await Task.Delay(ParseDelayMs());

                        string url = i == 0
                            ? $"{host}/api/?f=latest&tz=UTC"
                            : $"{host}/api/?f=latest&tz=UTC&p={i}";

                        string json = await HttpClient.Get(
                            url,
                            encoding: Encoding.UTF8,
                            addHeaders: JsonHeaders(),
                            useproxy: UseProxy(),
                            timeoutSeconds: 30);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            ParserLog.Write(TrackerName, "latest empty response", new Dictionary<string, object> { { "pageIndex", i } });
                            break;
                        }

                        if (SubsPleaseParser.IsLimitReached(json))
                        {
                            ParserLog.Write(TrackerName, "latest limit_reached", new Dictionary<string, object> { { "pageIndex", i } });
                            break;
                        }

                        var torrents = SubsPleaseParser.ParseLatestOrSearchJson(json, host);
                        var stats = await UpsertAsync(torrents);
                        totalParsed += stats.parsed;
                        totalAdded += stats.added;
                        totalUpdated += stats.updated;
                        totalSkipped += stats.skipped;
                        totalFailed += stats.failed;

                        ParserLog.Write(TrackerName, $"latest page done", new Dictionary<string, object>
                        {
                            { "pageIndex", i },
                            { "parsed", stats.parsed },
                            { "added", stats.added },
                            { "updated", stats.updated }
                        });
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

        public async Task<string> ParseShowsAsync(int limit = DefaultShowLimit, bool reset = false)
        {
            if (string.IsNullOrEmpty(Host()))
                return TrackerSyncHelpers.DisabledResult;

            return await TrackerSyncHelpers.RunParseAsync($"{TrackerName}-shows", ShowsLock, checkDisabled: false, async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    int take = Math.Clamp(limit <= 0 ? DefaultShowLimit : limit, 1, MaxShowLimit);
                    string host = Host();
                    var state = LoadCheckpoint(reset);

                    ParserLog.Write(TrackerName, "Starting ParseShows", new Dictionary<string, object>
                    {
                        { "limit", take },
                        { "reset", reset },
                        { "cursor", state.cursor },
                        { "knownShows", state.shows?.Count ?? 0 },
                        { "host", host }
                    });

                    // Schedule priority slugs
                    string scheduleJson = await HttpClient.Get(
                        $"{host}/api/?f=schedule&tz=UTC",
                        encoding: Encoding.UTF8,
                        addHeaders: JsonHeaders(),
                        useproxy: UseProxy(),
                        timeoutSeconds: 30);
                    var scheduleSlugs = SubsPleaseParser.ParseSchedulePageSlugs(scheduleJson ?? "");
                    state.schedulePrioritySlugs = scheduleSlugs;
                    PersistCheckpoint(state);

                    // Catalog index
                    if (ParseDelayMs() > 0)
                        await Task.Delay(ParseDelayMs());
                    string indexHtml = await HttpClient.Get(
                        $"{host}/shows/",
                        encoding: Encoding.UTF8,
                        useproxy: UseProxy(),
                        timeoutSeconds: 45);
                    var catalogSlugs = SubsPleaseParser.ParseShowSlugsFromIndexHtml(indexHtml ?? "");

                    // Merge: schedule first, then catalog order, unique
                    var workOrder = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string s in scheduleSlugs.Concat(catalogSlugs))
                    {
                        if (string.IsNullOrWhiteSpace(s))
                            continue;
                        if (seen.Add(s))
                            workOrder.Add(s);
                    }

                    if (workOrder.Count == 0)
                    {
                        ParserLog.Write(TrackerName, "ParseShows: empty show list");
                        return "ok";
                    }

                    // Ensure show entries exist for catalog
                    foreach (string slug in workOrder)
                        EnsureShowEntry(state, slug);

                    int start = Math.Clamp(state.cursor, 0, workOrder.Count);
                    int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;
                    int processed = 0;

                    for (int i = 0; i < take && start + i < workOrder.Count; i++)
                    {
                        string slug = workOrder[start + i];
                        if (processed > 0 && ParseDelayMs() > 0)
                            await Task.Delay(ParseDelayMs());

                        var entry = EnsureShowEntry(state, slug);
                        string sid = entry.sid;
                        if (string.IsNullOrWhiteSpace(sid))
                        {
                            string showHtml = await HttpClient.Get(
                                $"{host}/shows/{slug}/",
                                encoding: Encoding.UTF8,
                                useproxy: UseProxy(),
                                timeoutSeconds: 30);
                            sid = SubsPleaseParser.ExtractShowSidFromHtml(showHtml ?? "");
                            if (string.IsNullOrWhiteSpace(sid))
                            {
                                ParserLog.Write(TrackerName, "sid missing", new Dictionary<string, object> { { "slug", slug } });
                                processed++;
                                continue;
                            }
                            entry.sid = sid;
                            PersistCheckpoint(state);
                            if (ParseDelayMs() > 0)
                                await Task.Delay(ParseDelayMs());
                        }

                        string showJson = await HttpClient.Get(
                            $"{host}/api/?f=show&tz=UTC&sid={Uri.EscapeDataString(sid)}",
                            encoding: Encoding.UTF8,
                            addHeaders: JsonHeaders(),
                            useproxy: UseProxy(),
                            timeoutSeconds: 30);

                        var torrents = SubsPleaseParser.ParseShowJson(showJson ?? "", host, slug, sid);
                        var stats = await UpsertAsync(torrents);
                        totalParsed += stats.parsed;
                        totalAdded += stats.added;
                        totalUpdated += stats.updated;
                        totalSkipped += stats.skipped;
                        totalFailed += stats.failed;

                        entry.title = torrents.FirstOrDefault()?.name ?? entry.title;
                        entry.lastFetched = DateTime.UtcNow.ToString("o");
                        entry.batchCount = torrents.Count(t => t.isBatch);
                        entry.episodeCount = torrents.Count(t => !t.isBatch);
                        entry.lastInfoHashes1080 = torrents
                            .Select(t => t.infoHash)
                            .Where(h => !string.IsNullOrWhiteSpace(h))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(64)
                            .ToList();

                        processed++;
                        state.cursor = start + processed;
                        state.updatedAt = DateTime.UtcNow.ToString("o");
                        PersistCheckpoint(state);

                        ParserLog.Write(TrackerName, "show done", new Dictionary<string, object>
                        {
                            { "slug", slug },
                            { "sid", sid },
                            { "parsed", stats.parsed },
                            { "batch", entry.batchCount },
                            { "episodes", entry.episodeCount }
                        });
                    }

                    if (state.cursor >= workOrder.Count)
                        state.cursor = 0;
                    state.updatedAt = DateTime.UtcNow.ToString("o");
                    PersistCheckpoint(state);

                    ParserLog.Write(TrackerName, $"ParseShows completed (took {sw.Elapsed.TotalSeconds:F1}s)",
                        new Dictionary<string, object>
                        {
                            { "processed", processed },
                            { "cursor", state.cursor },
                            { "catalog", workOrder.Count },
                            { "parsed", totalParsed },
                            { "added", totalAdded },
                            { "updated", totalUpdated },
                            { "skipped", totalSkipped },
                            { "failed", totalFailed }
                        });
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"ParseShows error: {ex.Message}");
                }

                return "ok";
            });
        }

        public string GetParseShowStatus()
        {
            var state = LoadCheckpoint(reset: false);
            return JsonConvert.SerializeObject(new
            {
                ok = true,
                updatedAt = state.updatedAt,
                cursor = state.cursor,
                shows = state.shows?.Count ?? 0,
                withSid = state.shows?.Count(s => !string.IsNullOrWhiteSpace(s.sid)) ?? 0,
                schedulePriority = state.schedulePrioritySlugs?.Count ?? 0,
                sample = state.shows?.Take(5).Select(s => new
                {
                    s.slug,
                    s.sid,
                    s.batchCount,
                    s.episodeCount,
                    s.lastFetched
                })
            }, Formatting.Indented);
        }

        async Task<(int parsed, int added, int updated, int skipped, int failed)> UpsertAsync(
            List<SubsPleaseDetails> torrents)
        {
            if (torrents == null || torrents.Count == 0)
                return (0, 0, 0, 0, 0);

            int parsed = torrents.Count;
            int added = 0, updated = 0, skipped = 0, failed = 0;

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (string.IsNullOrWhiteSpace(t.magnet))
                {
                    failed++;
                    ParserLog.WriteFailed(TrackerName, t, "empty magnet");
                    return false;
                }

                bool exists = db.TryGetValue(t.url, out TorrentDetails cached);
                if (exists)
                {
                    bool sameMagnet = string.Equals(cached.magnet, t.magnet, StringComparison.OrdinalIgnoreCase);
                    bool sameTitle = string.Equals(cached.title?.Trim(), t.title?.Trim(), StringComparison.Ordinal);
                    bool sameSize = string.Equals(cached.sizeName, t.sizeName, StringComparison.Ordinal);
                    if (sameMagnet && sameTitle && sameSize)
                    {
                        skipped++;
                        ParserLog.WriteSkipped(TrackerName, cached, "no changes");
                        return false;
                    }

                    updated++;
                    ParserLog.WriteUpdated(TrackerName, t, "magnet/title/size refreshed");
                    return true;
                }

                added++;
                ParserLog.WriteAdded(TrackerName, t);
                return true;
            });

            return (parsed, added, updated, skipped, failed);
        }

        static ShowCheckpointEntry EnsureShowEntry(ShowsCheckpoint state, string slug)
        {
            state.shows ??= new List<ShowCheckpointEntry>();
            var entry = state.shows.FirstOrDefault(s =>
                string.Equals(s.slug, slug, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
                return entry;

            entry = new ShowCheckpointEntry { slug = slug };
            state.shows.Add(entry);
            return entry;
        }

        static ShowsCheckpoint LoadCheckpoint(bool reset)
        {
            lock (CheckpointFileLock)
            {
                if (reset || !IO.File.Exists(CheckpointPath))
                {
                    var fresh = new ShowsCheckpoint
                    {
                        updatedAt = DateTime.UtcNow.ToString("o"),
                        shows = new List<ShowCheckpointEntry>(),
                        cursor = 0,
                        schedulePrioritySlugs = new List<string>()
                    };
                    TryWriteCheckpoint(fresh);
                    return fresh;
                }

                try
                {
                    var state = JsonConvert.DeserializeObject<ShowsCheckpoint>(IO.File.ReadAllText(CheckpointPath));
                    if (state == null)
                        return new ShowsCheckpoint { shows = new List<ShowCheckpointEntry>() };
                    state.shows ??= new List<ShowCheckpointEntry>();
                    state.schedulePrioritySlugs ??= new List<string>();
                    return state;
                }
                catch
                {
                    return new ShowsCheckpoint { shows = new List<ShowCheckpointEntry>() };
                }
            }
        }

        static void PersistCheckpoint(ShowsCheckpoint state)
        {
            lock (CheckpointFileLock)
            {
                TryWriteCheckpoint(state);
            }
        }

        static void TryWriteCheckpoint(ShowsCheckpoint state)
        {
            try
            {
                string dir = IO.Path.GetDirectoryName(CheckpointPath);
                if (!string.IsNullOrEmpty(dir))
                    IO.Directory.CreateDirectory(dir);
                IO.File.WriteAllText(CheckpointPath, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch
            {
                // best-effort
            }
        }

        public class ShowsCheckpoint
        {
            public string updatedAt { get; set; }
            public List<ShowCheckpointEntry> shows { get; set; }
            public int cursor { get; set; }
            public List<string> schedulePrioritySlugs { get; set; }
        }

        public class ShowCheckpointEntry
        {
            public string slug { get; set; }
            public string sid { get; set; }
            public string title { get; set; }
            public string lastFetched { get; set; }
            public int batchCount { get; set; }
            public int episodeCount { get; set; }
            public List<string> lastInfoHashes1080 { get; set; }
        }
    }
}
