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
using JacRed.Models.tParse;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.NNMClub
{
    public class NNMClubSyncService
    {
        const string TrackerName = "nnmclub";

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static NNMClubSyncService()
        {
            if (IO.File.Exists("Data/temp/nnmclub_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/nnmclub_taskParse.json"));
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
                    // 10 - Новинки кино          | Фильмы
                    // 13 - Наше кино             | Фильмы
                    // 6  - Зарубежное кино       | Фильмы
                    // 4  - Наши сериалы          | Сериалы
                    // 3  - Зарубежные сериалы    | Сериалы
                    // 22 - Док. TV-бренды        | Док. сериалы, Док. фильмы
                    // 23 - Док. и телепередачи   | Док. сериалы, Док. фильмы
                    // 1  - Аниме и Манга         | Аниме
                    // 7  - Детям и родителям     | Мультфильмы, Мультсериалы
                    // 11 - HD, UHD и 3D Кино     | Фильмы
                    foreach (string cat in new List<string>() { "10", "13", "6", "4", "3", "22", "23", "1", "7", "11" })
                    {
                        string pageUrl = $"{baseUrl}?c={cat}&start={page * 20}";
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

        public async Task<string> UpdateTasksParseAsync()
        {
            foreach (string cat in new List<string>() { "10", "13", "6", "4", "3", "22", "23", "1", "7", "11" })
            {
                string html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php?c={cat}", encoding: Encoding.GetEncoding(1251), timeoutSeconds: 10, useproxy: AppInit.conf.NNMClub.useproxy);
                if (html == null || !html.Contains("NNM-Club</title>"))
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, "<a href=\"[^\"]+\">([0-9]+)</a>[^<\n\r]+<a href=\"[^\"]+\">След.</a>").Groups[1].Value, out int maxpages);

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

            IO.File.WriteAllText("Data/temp/nnmclub_taskParse.json", JsonConvert.SerializeObject(taskParse));
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

                        await Task.Delay(AppInit.conf.NNMClub.parseDelay);

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
                            await Task.Delay(AppInit.conf.NNMClub.parseDelay);

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
            string html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php?c={cat}&start={page * 20}", encoding: Encoding.GetEncoding(1251), useproxy: AppInit.conf.NNMClub.useproxy);
            if (html == null || !html.Contains("NNM-Club</title>"))
                return false;

            var torrents = NNMClubParser.ParseTorrentsFromPage(html, cat);

            FileDB.AddOrUpdate(torrents);
            return torrents.Count > 0;
        }
    }
}
