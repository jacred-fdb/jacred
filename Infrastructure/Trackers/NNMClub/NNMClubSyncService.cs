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

namespace JacRed.Infrastructure.Trackers.NNMClub
{
    public class NNMClubSyncService
    {
        const string TrackerName = "nnmclub";
        const string TaskParsePath = "Data/temp/nnmclub_taskParse.json";

        /// <summary>Portal page size; URL uses start={page * PageSize}.</summary>
        public const int PageSize = 25;

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerWorkFlag _updateTasksWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static NNMClubSyncService()
        {
            if (IO.File.Exists(TaskParsePath))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText(TaskParsePath));
        }

        static void PersistTaskParse()
        {
            try { IO.File.WriteAllText(TaskParsePath, JsonConvert.SerializeObject(taskParse)); }
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
                    string baseUrl = $"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php";
                    ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");

                    foreach (string cat in NNMClubCategories.Ids)
                    {
                        string pageUrl = $"{baseUrl}?c={cat}&start={page * PageSize}";
                        ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                        await parsePage(cat, page);
                        log += $"{cat} - {page}\n";
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
            // After PageSize 20→25, regenerate taskParse via this endpoint so page indices match the portal.
            // Cap at MaxPortalPages: NNMClub redirects older portal offsets to FAQ t=1626984.
            return Task.FromResult(TrackerSyncHelpers.RunUpdateTasksParseInBackground(TrackerName, _updateTasksWork, checkDisabled: false, async ct =>
            {
                foreach (string cat in NNMClubCategories.Ids)
                {
                    ct.ThrowIfCancellationRequested();

                    string html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php?c={cat}", encoding: Encoding.GetEncoding(1251), timeoutSeconds: 10, useproxy: AppInit.conf.NNMClub.useproxy, cancellationToken: ct);
                    if (html == null || !html.Contains("NNM-Club</title>"))
                        continue;

                    // Максимальное количиство страниц
                    int.TryParse(Regex.Match(html, "<a href=\"[^\"]+\">([0-9]+)</a>[^<\n\r]+<a href=\"[^\"]+\">След.</a>").Groups[1].Value, out int maxpages);
                    int taskCount = NNMClubPortalPagination.ClampTaskPageCount(maxpages);

                    if (!taskParse.ContainsKey(cat))
                        taskParse.Add(cat, new List<TaskParse>());

                    var val = taskParse[cat];
                    int added = 0;

                    // Загружаем список страниц в список задач (0 .. taskCount-1, capped at MaxPortalPages)
                    for (int page = 0; page < taskCount; page++)
                    {
                        try
                        {
                            if (val.FirstOrDefault(i => i.page == page) == null)
                            {
                                val.Add(new TaskParse(page));
                                added++;
                            }
                        }
                        catch { }
                    }

                    int pruned = NNMClubPortalPagination.PruneTasksBeyondPortalLimit(val);
                    ParserLog.Write(TrackerName, $"UpdateTasksParse cat={cat}: pagerMax={maxpages}, taskCount={taskCount}, added={added}, pruned={pruned}, total={val.Count}");
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
                    var pending = taskParse.ToArray()
                        .SelectMany(t => t.Value.Where(v => DateTime.Today != v.updateTime).Select(v => (cat: t.Key, val: v)))
                        .ToArray();
                    int done = 0;
                    TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", 0, pending.Length);

                    foreach (var item in pending)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(AppInit.conf.NNMClub.parseDelay, ct);

                        var status = await parsePage(item.cat, item.val.page, ct);
                        if (NNMClubPortalPagination.ShouldSettleTask(status))
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

        public async Task<string> ParseLatestAsync(int pages = 5)
        {
            return await TrackerSyncHelpers.RunParseLatestAsync(TrackerName, _parseLatestLock, checkDisabled: false, async () =>
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
                            await Task.Delay(AppInit.conf.NNMClub.parseDelay);

                            var status = await parsePage(task.Key, val.page);
                            if (NNMClubPortalPagination.ShouldSettleTask(status))
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
            });
        }

        async Task<NNMClubPortalPagination.PageParseStatus> parsePage(string cat, int page, CancellationToken cancellationToken = default)
        {
            string html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php?c={cat}&start={page * PageSize}", encoding: Encoding.GetEncoding(1251), useproxy: AppInit.conf.NNMClub.useproxy, cancellationToken: cancellationToken);
            if (html == null || !html.Contains("NNM-Club</title>"))
                return NNMClubPortalPagination.PageParseStatus.TransientError;

            if (NNMClubPortalPagination.IsPortalLimitFaq(html))
            {
                ParserLog.Write(TrackerName, $"{cat}/{page} portal limit FAQ");
                return NNMClubPortalPagination.PageParseStatus.PortalLimitFaq;
            }

            var torrents = NNMClubParser.ParseTorrentsFromPage(html, cat);
            FileDB.AddOrUpdate(torrents);

            return NNMClubPortalPagination.ClassifyPage(html, torrents.Count);
        }
    }
}
