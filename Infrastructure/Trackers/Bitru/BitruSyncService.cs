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

namespace JacRed.Infrastructure.Trackers.Bitru
{
    public class BitruSyncService
    {
        const string TrackerName = "bitru";

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static BitruSyncService()
        {
            if (IO.File.Exists("Data/temp/bitru_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/bitru_taskParse.json"));
        }

        public async Task<string> ParseAsync(int page = 1, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = $"{AppInit.conf.Bitru.rqHost()}/browse.php";
                    ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                    foreach (string cat in new List<string>() { "movie", "serial" })
                    {
                        string pageUrl = $"{baseUrl}?tmp={cat}&page={page}";
                        ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                        await BitruParser.ParsePageAsync(cat, page);
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

        public async Task<string> UpdateTasksParseAsync(CancellationToken cancellationToken = default)
        {
            foreach (string cat in new List<string>() { "movie", "serial" })
            {
                string html = await HttpClient.Get($"{AppInit.conf.Bitru.rqHost()}/browse.php?tmp={cat}", timeoutSeconds: 10, useproxy: AppInit.conf.Bitru.useproxy);
                if (html == null)
                    continue;

                int.TryParse(System.Text.RegularExpressions.Regex.Match(html, $"<a href=\"browse.php\\?tmp={cat}&page=[^\"]+\">([0-9]+)</a></div>").Groups[1].Value, out int maxpages);

                if (maxpages == 0)
                    maxpages = 1;

                for (int page = 1; page <= maxpages; page++)
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

            IO.File.WriteAllText("Data/temp/bitru_taskParse.json", JsonConvert.SerializeObject(taskParse));
            return "ok";
        }

        public async Task<string> ParseAllTaskAsync(CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAllTaskAsync(TrackerName, _parseAllTaskWork, checkDisabled: false, async () =>
            {
                foreach (var task in taskParse.ToArray())
                {
                    foreach (var val in task.Value.ToArray())
                    {
                        if (DateTime.Today == val.updateTime)
                            continue;

                        await Task.Delay(AppInit.conf.Bitru.parseDelay, cancellationToken);

                        bool res = await BitruParser.ParsePageAsync(task.Key, val.page);
                        if (res)
                            val.updateTime = DateTime.Today;
                    }
                }
            }, cancellationToken);
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

                    foreach (var task in taskParse.ToArray())
                    {
                        var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();

                        foreach (var val in pagesToParse)
                        {
                            await Task.Delay(AppInit.conf.Bitru.parseDelay, cancellationToken);

                            bool res = await BitruParser.ParsePageAsync(task.Key, val.page);
                            if (res)
                            {
                                val.updateTime = DateTime.Today;
                                log.AppendLine($"{task.Key} - {val.page}");
                            }
                        }
                    }

                    ParserLog.Write(TrackerName, $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (System.Net.Http.HttpRequestException ex)
                {
                    ParserLog.Write(TrackerName, $"ParseLatest HTTP Error: {ex.Message}");
                }
                catch (TaskCanceledException ex)
                {
                    ParserLog.Write(TrackerName, $"ParseLatest Cancelled: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    ParserLog.Write(TrackerName, $"ParseLatest Invalid Operation: {ex.Message}");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"ParseLatest Unexpected Error: {ex.Message}");
                }

                return log.ToString();
            }, cancellationToken);
        }
    }
}
