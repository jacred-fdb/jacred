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
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.Rutracker
{
    public class RutrackerSyncService
    {
        const string TrackerName = "rutracker";

        readonly IMemoryCache _memoryCache;

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly HashSet<string> firstPageCats = new HashSet<string>()
        {
            // 3D Кинофильмы
            "549",

            // Наше кино
            "22", "1666", "941",

            // Зарубежное кино
            "1950", "2090", "2221", "2091", "2092", "2093", "2200", "2540", "934", "505", "252",

            // Арт-хаус и авторское кино
            "124",

            // 3D Мультфильмы
            "1213",

            // Мультфильмы
            "2343",  "930", "2365", "208", "539", "209",

            // Мультсериалы
            "921", "815", "1460",

            // HD Video
            "1457", "2199", "313", "312", "1247", "2201", "2339", "140",

            // Зарубежные сериалы
            "842", "235", "242", "819", "1531", "721", "1102", "1120", "1214", "489", "387",

            // Русские сериалы
            "9", "81",

            // Корейские и Японские сериалы
            "915", "1939",

            // Зарубежные сериалы (HD Video)
            "119", "1803", "266", "193", "1690", "1459", "825", "1248", "1288",

            // Сериалы Латинской Америки, Турции и Индии
            "325", "534", "694", "704",

            // Аниме
            "1105", "2491", "1389"
        };

        static string Cookie;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static RutrackerSyncService()
        {
            if (IO.File.Exists("Data/temp/rutracker_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/rutracker_taskParse.json"));
        }

        public RutrackerSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        async ValueTask<bool> TakeLogin()
        {
            string authKey = "rutracker:TakeLogin()";
            if (_memoryCache.TryGetValue(authKey, out _))
                return false;

            _memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(2));

            try
            {
                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                    var postParams = new Dictionary<string, string>
                    {
                        { "login_username", AppInit.conf.Rutracker.login.u },
                        { "login_password", AppInit.conf.Rutracker.login.p },
                        { "login", "Вход" }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{AppInit.conf.Rutracker.host}/forum/login.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string session = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("bb_session="))
                                        session = new Regex("bb_session=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(session))
                                {
                                    Cookie = $"bb_ssl=1; bb_session={session};";
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        public async Task<string> ParseAsync(int page)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = $"{AppInit.conf.Rutracker.rqHost()}/forum/viewforum.php";
                    ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                    foreach (string cat in firstPageCats)
                    {
                        string pageUrl = page == 0 ? $"{baseUrl}?f={cat}" : $"{baseUrl}?f={cat}&start={page * 50}";
                        ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                        bool result = await parsePage(cat, page);
                        log += $"{cat} - {page} - {result}\n";
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
            foreach (string cat in new HashSet<string>(firstPageCats)
            {
                // Документальные фильмы
                "709", "2109",

                // Документалистика
                "46", "671", "2177", "2538", "251", "98", "97", "851", "2178", "821", "2076", "56", "2123", "876", "2139", "1467", "1469", "249", "552", "500", "2112", "1327", "1468", "2168", "2160", "314", "1281", "2110", "979", "2169", "2164", "2166", "2163",

                // Развлекательные телепередачи и шоу, приколы и юмор
                "24", "1959", "939", "1481", "113", "115", "882", "1482", "393", "2537", "532", "827",

                // Спорт
                "1392", "2475", "2493", "2113", "2482", "2103", "2522", "2485", "2486", "2479", "2089", "1794", "845", "2312", "343", "2111", "1527", "2069", "1323", "2009", "2000", "2010", "2006", "2007", "2005", "259", "2004", "1999", "2001", "2002", "283", "1997", "2003", "1608", "1609", "2294", "1229", "1693",
                "2532", "136", "592", "2533", "1952", "1621", "2075", "1668", "1613", "1614", "1623", "1615", "1630", "2425", "2514", "1616", "2014", "1442", "1491", "1987", "1617", "1620", "1998", "1343", "751", "1697", "255", "260", "261", "256", "1986", "660", "1551", "626", "262", "1326", "978", "1287", "1188", "1667",
                "1675", "257", "875", "263", "2073", "550", "2124", "1470", "528", "486", "854", "2079", "1336", "2171", "1339", "2455", "1434", "2350", "1472", "2068", "2016"
            })
            {
                try
                {
                    // Получаем html
                    string html = await HttpClient.Get($"{AppInit.conf.Rutracker.rqHost()}/forum/viewforum.php?f={cat}", useproxy: AppInit.conf.Rutracker.useproxy);
                    if (html == null)
                        continue;

                    // Максимальное количиство страниц
                    int.TryParse(Regex.Match(html, "Страница <b>1</b> из <b>([0-9]+)</b>").Groups[1].Value, out int maxpages);

                    if (maxpages > 0)
                    {
                        // Загружаем список страниц в список задач
                        for (int page = 0; page <= maxpages; page++)
                        {
                            if (!taskParse.ContainsKey(cat))
                                taskParse.Add(cat, new List<TaskParse>());

                            var val = taskParse[cat];
                            if (val.FirstOrDefault(i => i.page == page) == null)
                                val.Add(new TaskParse(page));
                        }
                    }
                    else
                    {
                        if (!taskParse.ContainsKey(cat))
                            taskParse.Add(cat, new List<TaskParse>());

                        var val = taskParse[cat];
                        if (val.FirstOrDefault(i => i.page == 1) == null)
                            val.Add(new TaskParse(1));
                    }
                }
                catch { }
            }

            IO.File.WriteAllText("Data/temp/rutracker_taskParse.json", JsonConvert.SerializeObject(taskParse));
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
                        await Task.Delay(AppInit.conf.Rutracker.parseDelay);

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
                            await Task.Delay(AppInit.conf.Rutracker.parseDelay);

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
            #region Авторизация
            //if (Cookie == null)
            //{
            //    if (await TakeLogin() == false)
            //        return false;
            //}
            #endregion

            string html = await HttpClient.Get($"{AppInit.conf.Rutracker.rqHost()}/forum/viewforum.php?f={cat}{(page == 0 ? "" : $"&start={page * 50}")}", /*cookie: Cookie, */useproxy: AppInit.conf.Rutracker.useproxy);
            if (html == null /*|| !html.Contains("id=\"logged-in-username\"")*/)
                return false;

            var torrents = RutrackerParser.ParseTorrentsFromPage(html, cat);

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                var fullNews = await HttpClient.Get(t.url, useproxy: AppInit.conf.Rutracker.useproxy);
                return RutrackerParser.ApplyTopicPageDetails(t, fullNews);
            });

            return torrents.Count > 0;
        }
    }
}
