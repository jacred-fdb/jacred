using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using IO = System.IO;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.Ultradox
{
    /// <summary>
    /// Ultradox sync. Nginx returns 503 unless Referer looks like google/yandex search.
    /// Listing magnets are empty — each row needs a detail fetch for real btih variants.
    /// </summary>
    public class UltradoxSyncService
    {
        const string TrackerName = UltradoxParser.TrackerName;
        const string TaskParsePath = "Data/temp/ultradox_taskParse.json";

        static readonly List<(string name, string val)> BrowserHeaders = new()
        {
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
            ("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7"),
            ("Cache-Control", "no-cache"),
            ("Pragma", "no-cache"),
            ("Sec-Fetch-Dest", "document"),
            ("Sec-Fetch-Mode", "navigate"),
            ("Sec-Fetch-Site", "cross-site"),
            ("Sec-Fetch-User", "?1"),
            ("Upgrade-Insecure-Requests", "1"),
        };

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerWorkFlag _updateTasksWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static UltradoxSyncService()
        {
            if (IO.File.Exists(TaskParsePath))
            {
                try
                {
                    taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(
                                    IO.File.ReadAllText(TaskParsePath))
                                ?? new Dictionary<string, List<TaskParse>>();
                }
                catch
                {
                    taskParse = new Dictionary<string, List<TaskParse>>();
                }
            }
        }

        static void PersistTaskParse()
        {
            try
            {
                string dir = IO.Path.GetDirectoryName(TaskParsePath);
                if (!string.IsNullOrEmpty(dir))
                    IO.Directory.CreateDirectory(dir);
                IO.File.WriteAllText(TaskParsePath, JsonConvert.SerializeObject(taskParse));
            }
            catch { }
        }

        static string Host() => (AppInit.conf.Ultradox?.rqHost() ?? "").TrimEnd('/');

        /// <summary>
        /// GET with google Referer (+ browser navigate headers). Own-origin Referer → 503.
        /// HttpClient follows redirects (ultradox.onl → numbered mirror).
        /// </summary>
        static ValueTask<string> FetchPageAsync(string url, CancellationToken cancellationToken) =>
            HttpClient.Get(
                url,
                encoding: Encoding.UTF8,
                referer: UltradoxParser.SearchEngineReferer,
                addHeaders: BrowserHeaders,
                useproxy: AppInit.conf.Ultradox.useproxy,
                cancellationToken: cancellationToken);

        /// <summary>Parse one listing page of every section (page≤0 = section root).</summary>
        public async Task<string> ParseAsync(int page, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                string host = Host();
                if (string.IsNullOrWhiteSpace(host))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Ultradox.host");
                    return "config missing";
                }

                var log = new StringBuilder();
                try
                {
                    var sw = Stopwatch.StartNew();
                    int totalFetched = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                    ParserLog.Write(TrackerName, "Starting parse", new Dictionary<string, object>
                    {
                        { "page", page },
                        { "host", host }
                    });

                    foreach (var kv in UltradoxCategories.Map)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (AppInit.conf.Ultradox.parseDelay > 0)
                            await Task.Delay(AppInit.conf.Ultradox.parseDelay, cancellationToken);

                        var (fetched, added, updated, skipped, failed) =
                            await ParseSectionPageAsync(host, kv.Key, kv.Value.Types, page, cancellationToken);

                        totalFetched += fetched;
                        totalAdded += added;
                        totalUpdated += updated;
                        totalSkipped += skipped;
                        totalFailed += failed;
                        log.AppendLine($"{kv.Key} - {page}");

                        ParserLog.Write(TrackerName, "Section page done", new Dictionary<string, object>
                        {
                            { "section", kv.Key },
                            { "page", page },
                            { "fetched", fetched },
                            { "added", added },
                            { "skipped", skipped },
                            { "failed", failed }
                        });
                    }

                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                        new Dictionary<string, object>
                        {
                            { "fetched", totalFetched },
                            { "added", totalAdded },
                            { "updated", totalUpdated },
                            { "skipped", totalSkipped },
                            { "failed", totalFailed }
                        });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (ex is OutOfMemoryException)
                        throw;

                    ParserLog.Write(TrackerName, "Error", new Dictionary<string, object>
                    {
                        { "message", ex.Message },
                        { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                    });
                }

                return log.Length == 0 ? "ok" : log.ToString();
            }, cancellationToken);
        }

        public Task<string> UpdateTasksParseAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TrackerSyncHelpers.RunUpdateTasksParseInBackground(TrackerName, _updateTasksWork, checkDisabled: true, async ct =>
            {
                string host = Host();
                if (string.IsNullOrWhiteSpace(host))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Ultradox.host");
                    return;
                }

                foreach (var kv in UltradoxCategories.Map)
                {
                    ct.ThrowIfCancellationRequested();

                    string html = await FetchPageAsync(UltradoxParser.ListingUrl(host, kv.Key, 0), ct);
                    if (string.IsNullOrEmpty(html))
                    {
                        ParserLog.Write(TrackerName, $"UpdateTasksParse {kv.Key}: empty response");
                        continue;
                    }

                    int maxPage = UltradoxParser.LastPageFromHtml(html);
                    if (!taskParse.ContainsKey(kv.Key))
                        taskParse[kv.Key] = new List<TaskParse>();

                    var val = taskParse[kv.Key];
                    // Go tasks are 1..maxPage (root is covered by page/1 on this site).
                    for (int page = 1; page <= maxPage; page++)
                    {
                        if (val.FirstOrDefault(i => i.page == page) == null)
                            val.Add(new TaskParse(page));
                    }

                    taskParse[kv.Key] = val.OrderBy(x => x.page).ToList();
                    ParserLog.Write(TrackerName, $"UpdateTasksParse {kv.Key}: maxPage={maxPage}, total={taskParse[kv.Key].Count}");
                }

                PersistTaskParse();
            }));
        }

        public Task<string> ParseAllTaskAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TrackerSyncHelpers.RunParseAllTaskInBackground(TrackerName, _parseAllTaskWork, checkDisabled: true, async ct =>
            {
                string host = Host();
                if (string.IsNullOrWhiteSpace(host))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Ultradox.host");
                    return;
                }

                if (taskParse.Count == 0)
                    await RebuildTasksAsync(host, ct);

                try
                {
                    var pending = taskParse.ToArray()
                        .SelectMany(t => t.Value.Where(v => DateTime.Today != v.updateTime)
                            .Select(v => (cat: t.Key, val: v)))
                        .ToArray();
                    int done = 0;
                    TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", 0, pending.Length);

                    foreach (var item in pending)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (AppInit.conf.Ultradox.parseDelay > 0)
                            await Task.Delay(AppInit.conf.Ultradox.parseDelay, ct);

                        if (!UltradoxCategories.Map.TryGetValue(item.cat, out var meta))
                            continue;
                        string[] types = meta.Types;

                        try
                        {
                            await ParseSectionPageAsync(host, item.cat, types, item.val.page, ct);
                            item.val.updateTime = DateTime.Today;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ParserLog.Write(TrackerName, $"ParseAllTask {item.cat} page={item.val.page} error: {ex.Message}");
                        }

                        done++;
                        TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", done, pending.Length, $"{item.cat}/{item.val.page}");
                    }
                }
                finally
                {
                    PersistTaskParse();
                }
            }));
        }

        public async Task<string> ParseLatestAsync(int pages = 5, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseLatestAsync(TrackerName, _parseLatestLock, checkDisabled: true, async () =>
            {
                string host = Host();
                if (string.IsNullOrWhiteSpace(host))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Ultradox.host");
                    return "config missing";
                }

                if (pages <= 0)
                    pages = 5;

                if (taskParse.Count == 0)
                    await RebuildTasksAsync(host, cancellationToken);

                var log = new StringBuilder();
                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName, $"Starting ParseLatest pages={pages}");

                    foreach (var task in taskParse.ToArray())
                    {
                        if (!UltradoxCategories.Map.TryGetValue(task.Key, out var meta) || meta?.Types == null)
                            continue;

                        var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();
                        foreach (var val in pagesToParse)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (AppInit.conf.Ultradox.parseDelay > 0)
                                await Task.Delay(AppInit.conf.Ultradox.parseDelay, cancellationToken);

                            try
                            {
                                await ParseSectionPageAsync(host, task.Key, meta.Types, val.page, cancellationToken);
                                val.updateTime = DateTime.Today;
                                log.AppendLine($"{task.Key} - {val.page}");
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                ParserLog.Write(TrackerName, $"ParseLatest f={task.Key} page={val.page} error: {ex.Message}");
                            }
                        }
                    }

                    PersistTaskParse();
                    ParserLog.Write(TrackerName, $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"ParseLatest Error: {ex.Message}");
                }

                return log.Length == 0 ? "ok" : log.ToString();
            }, cancellationToken);
        }

        async Task RebuildTasksAsync(string host, CancellationToken ct)
        {
            foreach (var kv in UltradoxCategories.Map)
            {
                ct.ThrowIfCancellationRequested();
                string html = await FetchPageAsync(UltradoxParser.ListingUrl(host, kv.Key, 0), ct);
                if (string.IsNullOrEmpty(html))
                    continue;

                int maxPage = UltradoxParser.LastPageFromHtml(html);
                if (!taskParse.ContainsKey(kv.Key))
                    taskParse[kv.Key] = new List<TaskParse>();

                var val = taskParse[kv.Key];
                for (int page = 1; page <= maxPage; page++)
                {
                    if (val.FirstOrDefault(i => i.page == page) == null)
                        val.Add(new TaskParse(page));
                }

                taskParse[kv.Key] = val.OrderBy(x => x.page).ToList();
            }

            PersistTaskParse();
        }

        async Task<(int fetched, int added, int updated, int skipped, int failed)> ParseSectionPageAsync(
            string host, string sectionPath, string[] types, int page, CancellationToken cancellationToken)
        {
            string listUrl = UltradoxParser.ListingUrl(host, sectionPath, page);
            string listHtml = await FetchPageAsync(listUrl, cancellationToken);

            if (string.IsNullOrEmpty(listHtml))
            {
                ParserLog.Write(TrackerName, "Listing fetch failed", new Dictionary<string, object>
                {
                    { "section", sectionPath },
                    { "page", page },
                    { "url", listUrl }
                });
                return (0, 0, 0, 0, 0);
            }

            var items = UltradoxParser.ParseListingHtml(listHtml);
            if (items.Count == 0)
                return (0, 0, 0, 0, 0);

            var torrents = new List<TorrentDetails>();
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AppInit.conf.Ultradox.parseDelay > 0)
                    await Task.Delay(AppInit.conf.Ultradox.parseDelay, cancellationToken);

                string detailUrl = item.DetailUrl;
                if (!detailUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    detailUrl = host + "/" + detailUrl.TrimStart('/');

                string detailHtml = await FetchPageAsync(detailUrl, cancellationToken);
                if (string.IsNullOrEmpty(detailHtml)
                    || !UltradoxParser.TryParseDetailHtml(detailHtml, out var variants, out var info))
                {
                    continue;
                }

                foreach (var variant in variants)
                {
                    var rec = UltradoxParser.BuildTorrent(host, sectionPath, types, item, variant, info);
                    if (rec != null)
                        torrents.Add(rec);
                }
            }

            return await SaveTorrentsAsync(torrents);
        }

        async Task<(int fetched, int added, int updated, int skipped, int failed)> SaveTorrentsAsync(
            List<TorrentDetails> torrents)
        {
            int fetched = torrents.Count;
            int added = 0, updated = 0, skipped = 0, failed = 0;

            if (torrents.Count == 0)
                return (0, 0, 0, 0, 0);

            torrents = torrents
                .Where(t => !string.IsNullOrWhiteSpace(t.name) && !string.IsNullOrWhiteSpace(t.magnet))
                .ToList();
            fetched = torrents.Count;
            if (fetched == 0)
                return (0, 0, 0, 0, 0);

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                await Task.CompletedTask;

                bool exists = db.TryGetValue(t.url, out TorrentDetails cached);
                bool needWrite = !exists
                    || !string.Equals(cached.title?.Trim(), t.title?.Trim(), StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(cached.magnet)
                    || !string.Equals(cached.magnet, t.magnet, StringComparison.OrdinalIgnoreCase);

                if (!needWrite)
                {
                    skipped++;
                    ParserLog.WriteSkipped(TrackerName, cached, "no changes");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(t.magnet))
                {
                    failed++;
                    ParserLog.WriteFailed(TrackerName, t, "empty magnet");
                    return false;
                }

                if (exists)
                {
                    updated++;
                    ParserLog.WriteUpdated(TrackerName, t, "magnet/title updated");
                }
                else
                {
                    added++;
                    ParserLog.WriteAdded(TrackerName, t);
                }

                return true;
            });

            return (fetched, added, updated, skipped, failed);
        }
    }
}
