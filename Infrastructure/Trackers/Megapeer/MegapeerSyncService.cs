using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.tParse;
using IO = System.IO;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.Megapeer
{
    public class MegapeerSyncService
    {
        const string TrackerName = "megapeer";
        const string TaskParsePath = "Data/temp/megapeer_taskParse.json";

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerWorkFlag _updateTasksWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static readonly string[] Categories = { "80", "79", "6", "5", "55", "57", "76" };

        static MegapeerSyncService()
        {
            if (IO.File.Exists(TaskParsePath))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText(TaskParsePath));
        }

        static void PersistTaskParse()
        {
            try { IO.File.WriteAllText(TaskParsePath, JsonConvert.SerializeObject(taskParse)); }
            catch { }
        }

        public async Task<string> ParseAsync(int page, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = $"{AppInit.conf.Megapeer.rqHost()}/browse.php";
                    ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                    foreach (string cat in Categories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string pageUrl = $"{baseUrl}?cat={cat}&page={page}";
                        ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                        bool res = await MegapeerParser.ParsePageAsync(cat, page, cancellationToken);
                        log += $"{cat} - {page} / {res}\n";
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
            return Task.FromResult(TrackerSyncHelpers.RunUpdateTasksParseInBackground(TrackerName, _updateTasksWork, checkDisabled: true, async ct =>
            {
                foreach (string cat in Categories)
                {
                    ct.ThrowIfCancellationRequested();

                    string html = await MegapeerParser.GetMegapeerBrowsePage($"{AppInit.conf.Megapeer.rqHost()}/browse.php?cat={cat}", cat, ct);

                    if (html == null)
                        continue;

                    int.TryParse(System.Text.RegularExpressions.Regex.Match(html, ">Всего: ([0-9]+)").Groups[1].Value, out int maxpages);
                    maxpages = maxpages / 50;

                    if (maxpages > 10)
                        maxpages = 10;

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
            return Task.FromResult(TrackerSyncHelpers.RunParseAllTaskInBackground(TrackerName, _parseAllTaskWork, checkDisabled: true, async ct =>
            {
                try
                {
                    var pending = taskParse.ToArray()
                        .SelectMany(t => t.Value.Where(v => DateTime.Today != v.updateTime).Select(v => (cat: t.Key, val: v)))
                        .ToArray();
                    int done = 0;
                    TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", 0, pending.Length);

                    foreach (var item in pending)
                    {
                        ct.ThrowIfCancellationRequested();

                        bool res = await MegapeerParser.ParsePageAsync(item.cat, item.val.page, ct);
                        if (res)
                            item.val.updateTime = DateTime.Today;

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
                var log = new StringBuilder();

                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName, $"Starting ParseLatest pages={pages}");

                    foreach (var task in taskParse.ToArray())
                    {
                        var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();

                        foreach (var val in pagesToParse)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            bool res = await MegapeerParser.ParsePageAsync(task.Key, val.page, cancellationToken);
                            if (res)
                            {
                                val.updateTime = DateTime.Today;
                                log.AppendLine($"{task.Key} - {val.page}");
                            }
                        }
                    }

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
