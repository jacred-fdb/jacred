using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.tParse;
using IO = System.IO;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.TorrentBy
{
    public class TorrentBySyncService
    {
        const string TrackerName = "torrentby";
        const string TaskParsePath = "Data/temp/torrentby_taskParse.json";
        static string CyclePath => ParseAllCycleStore.CyclePathForTracker(TrackerName);

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerWorkFlag _updateTasksWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static TorrentBySyncService()
        {
            if (IO.File.Exists(TaskParsePath))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText(TaskParsePath));
        }

        static void PersistTaskParse()
        {
            try { ParseAllCycleStore.WriteJsonAtomic(TaskParsePath, taskParse); }
            catch { }
        }

        public async Task<string> ParseAsync(int page, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = AppInit.conf.TorrentBy.rqHost();
                    ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                    foreach (string cat in TorrentByCategories.Ids)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string pageUrl = $"{baseUrl}/{cat}/?page={page}";
                        ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                        await TorrentByParser.ParsePageAsync(cat, page);
                        log += $"{cat} - {page}\n";
                    }
                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            }, cancellationToken);
        }

        public Task<string> UpdateTasksParseAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TrackerSyncHelpers.RunUpdateTasksParseInBackground(TrackerName, _updateTasksWork, checkDisabled: false, async ct =>
            {
                foreach (string cat in TorrentByCategories.Ids)
                {
                    ct.ThrowIfCancellationRequested();

                    string html = await HttpClient.Get($"{AppInit.conf.TorrentBy.rqHost()}/{cat}/", timeoutSeconds: 10, useproxy: AppInit.conf.TorrentBy.useproxy, cancellationToken: ct);
                    if (html == null)
                        continue;

                    int.TryParse(System.Text.RegularExpressions.Regex.Match(html, "href=\"\\?page=([0-9]+)\">[0-9]+</a>([\t ]+)?</center></td>").Groups[1].Value, out int maxpages);

                    for (int page = 0; page <= maxpages; page++)
                    {
                        try
                        {
                            if (!taskParse.ContainsKey(cat))
                                taskParse.Add(cat, new List<TaskParse>());

                            var val = taskParse[cat];
                            if (val.FirstOrDefault(i => i.page == page) == null)
                                val.Add(new TaskParse(page));
                        }
                        catch { }
                    }
                }

                PersistTaskParse();
            }));
        }

        public Task<string> ParseAllTaskAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TrackerSyncHelpers.RunParseAllTaskInBackground(TrackerName, _parseAllTaskWork, checkDisabled: false, async ct =>
            {
                try
                {
                    var (cycle, mapCount, pendingCount) = ParseAllCycleStore.BeginFlatFullRun(TrackerName, taskParse);
                    ParserLog.Write(TrackerName, $"ParseAllTask start {ParseAllCycleStore.FormatStartLog(cycle, pendingCount, mapCount)}");

                    var pending = taskParse.ToArray()
                        .SelectMany(t => t.Value.Where(v => ParseAllCycleStore.IsPendingInCycle(v, cycle)).Select(v => (cat: t.Key, val: v)))
                        .ToArray();
                    int done = 0;
                    TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", 0, pending.Length);

                    foreach (var item in pending)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(AppInit.conf.TorrentBy.parseDelay, ct);

                        bool res = await TorrentByParser.ParsePageAsync(item.cat, item.val.page, ct);
                        if (res)
                        {
                            ParseAllCycleStore.MarkDoneInCycle(item.val, cycle);
                            ParseAllCycleStore.PersistAfterPage(CyclePath, cycle, TaskParsePath, taskParse, persistCycle: true);
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
            return await TrackerSyncHelpers.RunParseLatestAsync(TrackerName, _parseLatestLock, checkDisabled: false, async () =>
            {
                var log = new StringBuilder();

                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName, $"Starting ParseLatest pages={pages}");

                    var cycle = ParseAllCycleStore.LoadFlatActiveCycle(TrackerName, taskParse);

                    foreach (var task in taskParse.ToArray())
                    {
                        var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();

                        foreach (var val in pagesToParse)
                        {
                            await Task.Delay(AppInit.conf.TorrentBy.parseDelay, cancellationToken);

                            bool res = await TorrentByParser.ParsePageAsync(task.Key, val.page);
                            if (res)
                            {
                                ParseAllCycleStore.MarkDoneInCycle(val, cycle);
                                log.AppendLine($"{task.Key} - {val.page}");
                            }
                        }
                    }

                    PersistTaskParse();
                    ParseAllCycleStore.SaveState(CyclePath, cycle);
                    ParserLog.Write(TrackerName, $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"ParseLatest Error: {ex.Message}");
                }

                return log.ToString();
            }, cancellationToken);
        }
    }
}
