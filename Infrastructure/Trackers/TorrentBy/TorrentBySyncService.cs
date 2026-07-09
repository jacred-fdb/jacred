using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Utils;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.tParse;
using IO = System.IO;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.TorrentBy
{
    public class TorrentBySyncService
    {
        const string TrackerName = "torrentby";

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static volatile bool _workParse;
        static volatile bool _parseAllTaskWork;
        static readonly SemaphoreSlim _parseLatestSemaphore = new SemaphoreSlim(1, 1);

        static readonly string[] Categories = { "films", "movies", "serials", "tv", "humor", "cartoons", "anime", "sport" };

        static TorrentBySyncService()
        {
            if (IO.File.Exists("Data/temp/torrentby_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/torrentby_taskParse.json"));
        }

        public async Task<string> ParseAsync(int page, CancellationToken cancellationToken = default)
        {
            if (_workParse)
                return "work";

            string log = "";
            _workParse = true;

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = AppInit.conf.TorrentBy.rqHost();
                ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                foreach (string cat in Categories)
                {
                    string pageUrl = $"{baseUrl}/{cat}/?page={page}";
                    ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                    await TorrentByParser.ParsePageAsync(cat, page);
                    log += $"{cat} - {page}\n";
                }
                ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write(TrackerName, $"Error: {ex.Message}");
            }
            finally
            {
                _workParse = false;
            }

            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }

        public async Task<string> UpdateTasksParseAsync(CancellationToken cancellationToken = default)
        {
            foreach (string cat in Categories)
            {
                string html = await HttpClient.Get($"{AppInit.conf.TorrentBy.rqHost()}/{cat}/", timeoutSeconds: 10, useproxy: AppInit.conf.TorrentBy.useproxy);
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

            IO.File.WriteAllText("Data/temp/torrentby_taskParse.json", JsonConvert.SerializeObject(taskParse));
            return "ok";
        }

        public async Task<string> ParseAllTaskAsync(CancellationToken cancellationToken = default)
        {
            if (_parseAllTaskWork)
                return "work";

            _parseAllTaskWork = true;

            try
            {
                foreach (var task in taskParse.ToArray())
                {
                    foreach (var val in task.Value.ToArray())
                    {
                        if (DateTime.Today == val.updateTime)
                            continue;

                        await Task.Delay(AppInit.conf.TorrentBy.parseDelay, cancellationToken);

                        bool res = await TorrentByParser.ParsePageAsync(task.Key, val.page);
                        if (res)
                            val.updateTime = DateTime.Today;
                    }
                }
            }
            catch { }

            _parseAllTaskWork = false;
            return "ok";
        }

        public async Task<string> ParseLatestAsync(int pages = 5, CancellationToken cancellationToken = default)
        {
            if (!await _parseLatestSemaphore.WaitAsync(0, cancellationToken))
                return "work";

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
                        await Task.Delay(AppInit.conf.TorrentBy.parseDelay, cancellationToken);

                        bool res = await TorrentByParser.ParsePageAsync(task.Key, val.page);
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
            finally
            {
                _parseLatestSemaphore.Release();
            }

            var logText = log.ToString();
            return string.IsNullOrWhiteSpace(logText) ? "ok" : logText;
        }
    }
}
