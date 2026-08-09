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

namespace JacRed.Infrastructure.Trackers.Anibelka
{
    /// <summary>
    /// Anibelka sync — anonymous only. Never login: passkeys must not enter magnets.
    /// </summary>
    public class AnibelkaSyncService
    {
        const string TrackerName = AnibelkaParser.TrackerName;
        const string TaskParsePath = "Data/temp/anibelka_taskParse.json";

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerWorkFlag _updateTasksWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static AnibelkaSyncService()
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

        static string Host() => (AppInit.conf.Anibelka?.rqHost() ?? "").TrimEnd('/');

        /// <summary>Parse one zero-based listing page of every section.</summary>
        public async Task<string> ParseAsync(int page, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                string host = Host();
                if (string.IsNullOrWhiteSpace(host))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Anibelka.host");
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

                    foreach (var kv in AnibelkaCategories.Map)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (AppInit.conf.Anibelka.parseDelay > 0)
                            await Task.Delay(AppInit.conf.Anibelka.parseDelay, cancellationToken);

                        var (fetched, added, updated, skipped, failed) =
                            await ParseSectionPageAsync(host, kv.Key, page, cancellationToken);

                        totalFetched += fetched;
                        totalAdded += added;
                        totalUpdated += updated;
                        totalSkipped += skipped;
                        totalFailed += failed;
                        log.AppendLine($"{kv.Key} - {page}");

                        ParserLog.Write(TrackerName, "Section page done", new Dictionary<string, object>
                        {
                            { "f", kv.Key },
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
                    ParserLog.Write(TrackerName, "Config missing — add Anibelka.host");
                    return;
                }

                foreach (var kv in AnibelkaCategories.Map)
                {
                    ct.ThrowIfCancellationRequested();

                    string html = await HttpClient.Get(
                        AnibelkaParser.ForumUrl(host, kv.Key, 0),
                        encoding: Encoding.UTF8,
                        useproxy: AppInit.conf.Anibelka.useproxy,
                        cancellationToken: ct);

                    if (string.IsNullOrEmpty(html))
                    {
                        ParserLog.Write(TrackerName, $"UpdateTasksParse f={kv.Key}: empty response");
                        continue;
                    }

                    int maxPage = AnibelkaParser.LastPageFromHtml(html);
                    if (!taskParse.ContainsKey(kv.Key))
                        taskParse[kv.Key] = new List<TaskParse>();

                    var val = taskParse[kv.Key];
                    for (int page = 0; page <= maxPage; page++)
                    {
                        if (val.FirstOrDefault(i => i.page == page) == null)
                            val.Add(new TaskParse(page));
                    }

                    taskParse[kv.Key] = val.OrderBy(x => x.page).ToList();
                    ParserLog.Write(TrackerName, $"UpdateTasksParse f={kv.Key}: maxPage={maxPage}, total={taskParse[kv.Key].Count}");
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
                    ParserLog.Write(TrackerName, "Config missing — add Anibelka.host");
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
                        if (AppInit.conf.Anibelka.parseDelay > 0)
                            await Task.Delay(AppInit.conf.Anibelka.parseDelay, ct);

                        try
                        {
                            await ParseSectionPageAsync(host, item.cat, item.val.page, ct);
                            // Empty listings still count as done (Go markPageToday).
                            item.val.updateTime = DateTime.Today;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ParserLog.Write(TrackerName, $"ParseAllTask f={item.cat} page={item.val.page} error: {ex.Message}");
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
                    ParserLog.Write(TrackerName, "Config missing — add Anibelka.host");
                    return "config missing";
                }

                if (pages <= 0)
                    pages = 1;

                var log = new StringBuilder();
                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName, $"Starting ParseLatest pages={pages}");

                    foreach (var kv in AnibelkaCategories.Map)
                    {
                        for (int page = 0; page < pages; page++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (AppInit.conf.Anibelka.parseDelay > 0)
                                await Task.Delay(AppInit.conf.Anibelka.parseDelay, cancellationToken);

                            await ParseSectionPageAsync(host, kv.Key, page, cancellationToken);
                            log.AppendLine($"{kv.Key} - {page}");
                        }
                    }

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
            foreach (var kv in AnibelkaCategories.Map)
            {
                ct.ThrowIfCancellationRequested();
                string html = await HttpClient.Get(
                    AnibelkaParser.ForumUrl(host, kv.Key, 0),
                    encoding: Encoding.UTF8,
                    useproxy: AppInit.conf.Anibelka.useproxy,
                    cancellationToken: ct);

                if (string.IsNullOrEmpty(html))
                    continue;

                int maxPage = AnibelkaParser.LastPageFromHtml(html);
                if (!taskParse.ContainsKey(kv.Key))
                    taskParse[kv.Key] = new List<TaskParse>();

                var val = taskParse[kv.Key];
                for (int page = 0; page <= maxPage; page++)
                {
                    if (val.FirstOrDefault(i => i.page == page) == null)
                        val.Add(new TaskParse(page));
                }

                taskParse[kv.Key] = val.OrderBy(x => x.page).ToList();
            }

            PersistTaskParse();
        }

        async Task<(int fetched, int added, int updated, int skipped, int failed)> ParseSectionPageAsync(
            string host, string sectionId, int page, CancellationToken cancellationToken)
        {
            string listUrl = AnibelkaParser.ForumUrl(host, sectionId, page);
            string listHtml = await HttpClient.Get(
                listUrl,
                encoding: Encoding.UTF8,
                useproxy: AppInit.conf.Anibelka.useproxy,
                cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(listHtml))
            {
                ParserLog.Write(TrackerName, "Listing fetch failed", new Dictionary<string, object>
                {
                    { "f", sectionId },
                    { "page", page },
                    { "url", listUrl }
                });
                return (0, 0, 0, 0, 0);
            }

            var items = AnibelkaParser.ParseListingHtml(listHtml);
            if (items.Count == 0)
                return (0, 0, 0, 0, 0);

            var torrents = new List<AnibelkaDetails>();
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AppInit.conf.Anibelka.parseDelay > 0)
                    await Task.Delay(AppInit.conf.Anibelka.parseDelay, cancellationToken);

                string topicUrl = AnibelkaParser.TopicUrl(host, item.TopicId);
                string topicHtml = await HttpClient.Get(
                    topicUrl,
                    encoding: Encoding.UTF8,
                    referer: listUrl,
                    useproxy: AppInit.conf.Anibelka.useproxy,
                    cancellationToken: cancellationToken);

                if (string.IsNullOrEmpty(topicHtml)
                    || !AnibelkaParser.TryParseTopicHtml(topicHtml, out var info))
                {
                    continue;
                }

                var (name, original, year) = AnibelkaParser.ParseTitle(item.Title);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                torrents.Add(new AnibelkaDetails
                {
                    trackerName = TrackerName,
                    types = new[] { "anime" },
                    url = topicUrl,
                    title = item.Title,
                    sid = info.Sid,
                    pir = info.Pir,
                    sizeName = info.SizeName,
                    createTime = info.CreateTime == default ? DateTime.UtcNow : info.CreateTime,
                    updateTime = DateTime.UtcNow,
                    name = name,
                    originalname = original,
                    relased = year,
                    downloadId = info.TorrentId
                });
            }

            return await SaveTorrentsAsync(torrents, host, cancellationToken);
        }

        async Task<(int fetched, int added, int updated, int skipped, int failed)> SaveTorrentsAsync(
            List<AnibelkaDetails> torrents, string host, CancellationToken cancellationToken)
        {
            int fetched = torrents.Count;
            int added = 0, updated = 0, skipped = 0, failed = 0;

            if (torrents.Count == 0)
                return (0, 0, 0, 0, 0);

            // Drop records with empty names before DB merge.
            torrents = torrents
                .Where(t => !string.IsNullOrWhiteSpace(t.name) && !string.IsNullOrWhiteSpace(t.downloadId))
                .ToList();
            fetched = torrents.Count;
            if (fetched == 0)
                return (0, 0, 0, 0, 0);

            await FileDB.AddOrUpdate(torrents, async (torrent, db) =>
            {
                var t = (AnibelkaDetails)torrent;
                bool exists = db.TryGetValue(t.url, out TorrentDetails cached);
                bool needMagnet = !exists
                    || !string.Equals(cached.title?.Trim(), t.title?.Trim(), StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(cached.magnet);

                if (!needMagnet)
                {
                    skipped++;
                    ParserLog.WriteSkipped(TrackerName, cached, "no changes");
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(t.downloadId))
                {
                    if (AppInit.conf.Anibelka.parseDelay > 0)
                        await Task.Delay(AppInit.conf.Anibelka.parseDelay, cancellationToken);

                    // Anonymous download — never send cookies/login.
                    byte[] torrentFile = await HttpClient.Download(
                        AnibelkaParser.TorrentDownloadUrl(host, t.downloadId),
                        referer: host,
                        useproxy: AppInit.conf.Anibelka.useproxy,
                        cancellationToken: cancellationToken);

                    if (torrentFile != null && torrentFile.Length > 0)
                    {
                        // Full magnet is OK: anonymous .torrent has no personal passkey.
                        string magnet = BencodeTo.Magnet(torrentFile);
                        if (!string.IsNullOrWhiteSpace(magnet))
                        {
                            t.magnet = magnet;
                            if (string.IsNullOrWhiteSpace(t.sizeName))
                            {
                                string sizeName = BencodeTo.SizeName(torrentFile);
                                if (!string.IsNullOrWhiteSpace(sizeName))
                                    t.sizeName = sizeName;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(t.magnet))
                {
                    failed++;
                    ParserLog.WriteFailed(TrackerName, t, "could not get magnet");
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
