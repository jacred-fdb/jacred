using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.tParse;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.Rutor
{
    public class RutorSyncService
    {
        const string TrackerName = "rutor";
        const string TaskParsePath = "Data/temp/rutor_taskParse.json";
        static string CyclePath => ParseAllCycleStore.CyclePathForTracker(TrackerName);

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerWorkFlag _updateTasksWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static RutorSyncService()
        {
            if (IO.File.Exists(TaskParsePath))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText(TaskParsePath));
        }

        static void PersistTaskParse()
        {
            try { ParseAllCycleStore.WriteJsonAtomic(TaskParsePath, taskParse); }
            catch { }
        }

        public async Task<string> ParseAsync(int page)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = $"{AppInit.conf.Rutor.rqHost()}/browse";
                    ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                    foreach (string cat in RutorCategories.Ids)
                    {
                        string pageUrl = $"{baseUrl}/{page}/{cat}/0/0";
                        ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                        bool res = await parsePage(cat, page);
                        log += $"{cat} - {page} / {res}\n";
                    }
                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            });
        }

        public Task<string> UpdateTasksParseAsync()
        {
            return Task.FromResult(TrackerSyncHelpers.RunUpdateTasksParseInBackground(TrackerName, _updateTasksWork, checkDisabled: false, async ct =>
            {
                foreach (string cat in RutorCategories.Ids)
                {
                    ct.ThrowIfCancellationRequested();

                    string html = await HttpClient.Get($"{AppInit.conf.Rutor.rqHost()}/browse/0/{cat}/0/0", useproxy: AppInit.conf.Rutor.useproxy, cancellationToken: ct);
                    if (html == null)
                        continue;

                    // Максимальное количиство страниц
                    int.TryParse(Regex.Match(html, "<a href=\"/browse/([0-9]+)/[0-9]+/[0-9]+/[0-9]+\"><b>[0-9]+&nbsp;-&nbsp;[0-9]+</b></a></p>").Groups[1].Value, out int maxpages);

                    // Загружаем список страниц в список задач
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

        public Task<string> ParseAllTaskAsync()
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
                        await Task.Delay(AppInit.conf.Rutor.parseDelay, ct);

                        bool res = await parsePage(item.cat, item.val.page, ct);
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

        public async Task<string> ParseLatestAsync(int pages = 5)
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
                            await Task.Delay(AppInit.conf.Rutor.parseDelay);

                            bool res = await parsePage(task.Key, val.page);
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
            });
        }

        async Task<bool> parsePage(string cat, int page, CancellationToken cancellationToken = default)
        {
            string html = await HttpClient.Get($"{AppInit.conf.Rutor.rqHost()}/browse/{page}/{cat}/0/0", useproxy: AppInit.conf.Rutor.useproxy, cancellationToken: cancellationToken);
            if (html == null)
                return false;

            var torrents = RutorParser.ParseTorrentsFromPage(html, cat);

            FileDB.AddOrUpdate(torrents);
            return torrents.Count > 0;
        }
    }
}
