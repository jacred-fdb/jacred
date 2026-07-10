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
using JacRed.Infrastructure.Utils;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.Rutor
{
    public class RutorSyncService
    {
        const string TrackerName = "rutor";

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static readonly List<string> Categories = new List<string>() { "1", "5", "4", "16", "12", "6", "7", "10", "17", "13", "15" };

        static RutorSyncService()
        {
            if (IO.File.Exists("Data/temp/rutor_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/rutor_taskParse.json"));
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
                    // 1  - Зарубежные фильмы          | Фильмы
                    // 5  - Наши фильмы                | Фильмы
                    // 4  - Зарубежные сериалы         | Сериалы
                    // 16 - Наши сериалы               | Сериалы
                    // 12 - Научно-популярные фильмы   | Док. сериалы, Док. фильмы
                    // 6  - Телевизор                  | ТВ Шоу
                    // 7  - Мультипликация             | Мультфильмы, Мультсериалы
                    // 10 - Аниме                      | Аниме
                    // 17 - Иностранные релизы         | Фильмы (UKR)
                    // 13 - Спорт и Здоровье           | Спорт
                    // 15 - Юмор                       | ТВ Шоу
                    foreach (string cat in Categories)
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

        public async Task<string> UpdateTasksParseAsync()
        {
            foreach (string cat in Categories)
            {
                string html = await HttpClient.Get($"{AppInit.conf.Rutor.rqHost()}/browse/0/{cat}/0/0", useproxy: AppInit.conf.Rutor.useproxy);
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

            IO.File.WriteAllText("Data/temp/rutor_taskParse.json", JsonConvert.SerializeObject(taskParse));
            return "ok";
        }

        public async Task<string> ParseAllTaskAsync()
        {
            return await TrackerSyncHelpers.RunParseAllTaskAsync(TrackerName, _parseAllTaskWork, checkDisabled: false, async () =>
            {
                foreach (var task in taskParse.ToArray())
                {
                    foreach (var val in task.Value.ToArray())
                    {
                        if (DateTime.Today == val.updateTime)
                            continue;

                        await Task.Delay(AppInit.conf.Rutor.parseDelay);

                        bool res = await parsePage(task.Key, val.page);
                        if (res)
                            val.updateTime = DateTime.Today;
                    }
                }
            });
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
                            await Task.Delay(AppInit.conf.Rutor.parseDelay);

                            bool res = await parsePage(task.Key, val.page);
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
            });
        }

        async Task<bool> parsePage(string cat, int page)
        {
            string html = await HttpClient.Get($"{AppInit.conf.Rutor.rqHost()}/browse/{page}/{cat}/0/0", useproxy: AppInit.conf.Rutor.useproxy);
            if (html == null)
                return false;

            var torrents = RutorParser.ParseTorrentsFromPage(html, cat);

            FileDB.AddOrUpdate(torrents);
            return torrents.Count > 0;
        }
    }
}
