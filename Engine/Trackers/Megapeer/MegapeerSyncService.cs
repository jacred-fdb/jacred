using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Engine.CORE;
using JacRed.Engine.Parsing;
using JacRed.Models.tParse;
using IO = System.IO;
using Newtonsoft.Json;

namespace JacRed.Engine.Trackers.Megapeer
{
    public class MegapeerSyncService
    {
        const string TrackerName = "megapeer";

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static volatile bool _workParse;
        static volatile bool _parseAllTaskWork;
        static readonly SemaphoreSlim _parseLatestSemaphore = new SemaphoreSlim(1, 1);

        static readonly string[] Categories = { "80", "79", "6", "5", "55", "57", "76" };

        static MegapeerSyncService()
        {
            if (IO.File.Exists("Data/temp/megapeer_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/megapeer_taskParse.json"));
        }

        static bool IsDisabled()
        {
            return AppInit.conf?.disable_trackers != null && AppInit.conf.disable_trackers.Contains(TrackerName, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<string> ParseAsync(int page, CancellationToken cancellationToken = default)
        {
            if (IsDisabled())
                return "disabled";
            if (_workParse)
                return "work";

            _workParse = true;
            string log = "";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = $"{AppInit.conf.Megapeer.rqHost()}/browse.php";
                ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                foreach (string cat in Categories)
                {
                    string pageUrl = $"{baseUrl}?cat={cat}&page={page}";
                    ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                    bool res = await MegapeerParser.ParsePageAsync(cat, page);
                    log += $"{cat} - {page} / {res}\n";
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
            if (IsDisabled())
                return "disabled";

            foreach (string cat in Categories)
            {
                string html = await MegapeerParser.GetMegapeerBrowsePage($"{AppInit.conf.Megapeer.rqHost()}/browse.php?cat={cat}", cat);

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

            IO.File.WriteAllText("Data/temp/megapeer_taskParse.json", JsonConvert.SerializeObject(taskParse));
            return "ok";
        }

        public async Task<string> ParseAllTaskAsync(CancellationToken cancellationToken = default)
        {
            if (IsDisabled())
                return "disabled";
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

                        bool res = await MegapeerParser.ParsePageAsync(task.Key, val.page);
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
            if (IsDisabled())
                return "disabled";
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
                        bool res = await MegapeerParser.ParsePageAsync(task.Key, val.page);
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
