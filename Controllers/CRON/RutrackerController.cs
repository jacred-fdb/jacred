using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using JacRed.Engine.CORE;
using JacRed.Models.tParse;
using IO = System.IO;
using JacRed.Engine;
using Microsoft.Extensions.Caching.Memory;
using JacRed.Models.Details;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/rutracker/[action]")]
    public class RutrackerController : BaseController
    {
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


        static RutrackerController()
        {
            if (IO.File.Exists("Data/temp/rutracker_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/rutracker_taskParse.json"));
        }

        #region Cookie / TakeLogin
        static string Cookie;

        async ValueTask<bool> TakeLogin()
        {
            string authKey = "rutracker:TakeLogin()";
            if (memoryCache.TryGetValue(authKey, out _))
                return false;

            memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(2));

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
        #endregion


        #region Parse
        static bool _workParse = false;

        async public Task<string> Parse(int page)
        {
            if (_workParse)
                return "work";

            _workParse = true;
            string log = "";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = $"{AppInit.conf.Rutracker.rqHost()}/forum/viewforum.php";
                ParserLog.Write("rutracker", $"Starting parse page={page}, base: {baseUrl}");
                foreach (string cat in firstPageCats)
                {
                    string pageUrl = page == 0 ? $"{baseUrl}?f={cat}" : $"{baseUrl}?f={cat}&start={page * 50}";
                    ParserLog.Write("rutracker", $"Category {cat}: {pageUrl}");
                    bool result = await parsePage(cat, page);
                    log += $"{cat} - {page} - {result}\n";
                }
                ParserLog.Write("rutracker", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("rutracker", $"Error: {ex.Message}");
            }
            finally
            {
                _workParse = false;
            }

            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }
        #endregion

        #region UpdateTasksParse
        async public Task<string> UpdateTasksParse()
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
        #endregion

        #region ParseAllTask
        static bool _parseAllTaskWork = false;

        async public Task<string> ParseAllTask()
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
                        await Task.Delay(AppInit.conf.Rutracker.parseDelay);

                        bool res = await parsePage(task.Key, val.page);
                        if (res)
                            val.updateTime = DateTime.Today;
                    }
                }
            }
            catch { }

            _parseAllTaskWork = false;
            return "ok";
        }
        #endregion

        #region ParseLatest
        static readonly SemaphoreSlim _parseLatestSemaphore = new SemaphoreSlim(1, 1);

        async public Task<string> ParseLatest(int pages = 5)
        {
            if (!await _parseLatestSemaphore.WaitAsync(0))
                return "work";

            string log = "";

            try
            {
                var sw = Stopwatch.StartNew();
                ParserLog.Write("rutracker", $"Starting ParseLatest pages={pages}");

                foreach (var task in taskParse.ToArray())
                {
                    // Get first N pages sorted by page number
                    var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();

                    foreach (var val in pagesToParse)
                    {
                        await Task.Delay(AppInit.conf.Rutracker.parseDelay);

                        bool res = await parsePage(task.Key, val.page);
                        if (res)
                        {
                            val.updateTime = DateTime.Today;
                            log += $"{task.Key} - {val.page}\n";
                        }
                    }
                }

                ParserLog.Write("rutracker", $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("rutracker", $"ParseLatest Error: {ex.Message}");
            }
            finally
            {
                _parseLatestSemaphore.Release();
            }

            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }
        #endregion


        #region parsePage
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

            var torrents = new List<TorrentBaseDetails>();

            foreach (string row in tParse.ReplaceBadNames(html).Split("class=\"torTopic\"").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                #region Дата создания
                DateTime.TryParse(Match("<p>([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})</p>"), out DateTime createTime);
                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("<a id=\"tt-([0-9]+)\"");
                string title = Match("<a id=\"tt-[0-9]+\"[^>]+>([^\n\r]+)</a>");
                title = Regex.Replace(title, "<[^>]+>", "");
                string _sid = Match("<span class=\"seedmed\"[^>]+><b>([0-9]+)</b>");
                string _pir = Match("<span class=\"leechmed\"[^>]+><b>([0-9]+)</b>");
                string sizeName = Match("dl-stub\">([^<]+)</a>").Replace("&nbsp;", " ");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                url = $"{AppInit.conf.Rutracker.host}/forum/viewtopic.php?t={url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat is "549" or "1213" or "2109" or "22" or "1666" or "941" or "1950" or "1950" or "2090" or "2221" or "2091" or "2092" or "2093" or "2200" or "2540" or "934" or "505" or "124" or "1457"
                                or "2199" or "313" or "312" or "1247" or "2201" or "2339" or "140" or "2343" or "930" or "2365" or "208" or "539" or "209" or "709" or "252")
                {
                    #region Фильмы
                    // Ниже нуля / Bajocero / Below Zero (Йуис Килес / Lluís Quílez) [2021, Испания, боевик, триллер, криминал, WEB-DLRip] MVO (MUZOBOZ) + Original (Spa) + Sub (Rus, Eng)
                    var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Белый тигр / The White Tiger (Рамин Бахрани / Ramin Bahrani) [2021, Индия, США, драма, криминал, WEB-DLRip] MVO (HDRezka Studio) + Sub (Rus, Eng) + Original Eng
                        g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Дневной дозор (Тимур Бекмамбетов) [2006, Россия, боевик, триллер, фэнтези, BDRip-AVC]
                            g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                            {
                                name = g[1].Value;
                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                    }
                    #endregion

                    if (name != null)
                        name = name.Replace("в 3Д", "").Trim();

                    if (originalname != null)
                        originalname = originalname.Replace(" in 3D", "").Replace(" 3D", "").Trim();
                }
                else if (cat is "842" or "235" or "242" or "819" or "1531" or "721" or "1102" or "1120" or "1214" or "489" or "387" or "9" or "81" or "119" or "1803" or "266" or "193" or "1690" or "1459" or "825" or "1248" or "1288"
                                      or "325" or "534" or "694" or "704" or "921" or "815" or "1460")
                {
                    #region Сериалы
                    if (Regex.IsMatch(title, "(Сезон|Серии)", RegexOptions.IgnoreCase))
                    {
                        if (title.Contains("Сезон:"))
                        {
                            // Голяк / Без гроша / Без денег / Brassic / Сезон: 4 / Серии: 1-8 из 8 (Джон Райт, Дэниэл О’Хара, Сауль Метцштайн, Джон Хардвик) [2022, Великобритания, Комедия, криминал, WEB-DLRip] MVO (Ozz) + Original + Sub (Rus, Ukr, Eng)
                            var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / [^/\\(\\[]+ / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Уравнитель / Великий уравнитель / The Equalizer / Сезон: 1 / Серии: 1-3 из 4 (Лиз Фридлендер, Солван Наим) [2021, США, Боевик, триллер, драма, криминал, детектив, WEB-DLRip] MVO (TVShows) + Original
                                g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                {
                                    name = g[1].Value;
                                    originalname = g[2].Value;

                                    if (int.TryParse(g[3].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // 911 служба спасения / 9-1-1 / Сезон: 4 / Серии: 1-6 из 9 (Брэдли Букер, Дженнифер Линч, Гвинет Хердер-Пэйтон) [2021, США, Боевик, триллер, драма, WEB-DLRip] MVO (LostFilm) + Original
                                    g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                    {
                                        name = g[1].Value;
                                        originalname = g[2].Value;

                                        if (int.TryParse(g[3].Value, out int _yer))
                                            relased = _yer;
                                    }
                                    else
                                    {
                                        // Петербургский роман / Сезон: 1 / Серии: 1-8 из 8 (Александр Муратов) [2018, мелодрама, HDTV 1080i]
                                        g = Regex.Match(title, "^([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                        {
                                            name = g[1].Value;
                                            if (int.TryParse(g[2].Value, out int _yer))
                                                relased = _yer;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Уравнитель / Великий уравнитель / The Equalizer / Серии: 1-3 из 4 (Лиз Фридлендер, Солван Наим) [2021, США, Боевик, триллер, драма, криминал, детектив, WEB-DLRip] MVO (TVShows) + Original
                            var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // 911 служба спасения / 9-1-1 / Серии: 1-6 из 9 (Брэдли Букер, Дженнифер Линч, Гвинет Хердер-Пэйтон) [2021, США, Боевик, триллер, драма, WEB-DLRip] MVO (LostFilm) + Original
                                g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                {
                                    name = g[1].Value;
                                    originalname = g[2].Value;

                                    if (int.TryParse(g[3].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // Петербургский роман / Серии: 1-8 из 8 (Александр Муратов) [2018, мелодрама, HDTV 1080i]
                                    g = Regex.Match(title, "^([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                    {
                                        name = g[1].Value;
                                        if (int.TryParse(g[2].Value, out int _yer))
                                            relased = _yer;
                                    }
                                }
                            }
                        }

                        if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase) || Regex.IsMatch(originalname ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        {
                            relased = 0;
                            name = null;
                            originalname = null;
                        }
                    }
                    #endregion
                }
                else if (cat is "1105" or "2491" or "1389" or "915" or "1939" or "46" or "671" or "2177" or "2538" or "251" or "98" or "97" or "851" or "2178" or "821" or "2076" or "56" or "2123" or "876" or "2139" or "1467"
                                       or "1469" or "249" or "552" or "500" or "2112" or "1327" or "1468" or "2168" or "2160" or "314" or "1281" or "2110" or "979" or "2169" or "2164" or "2166" or "2163"
                                       or "24" or "1959" or "939" or "1481" or "113" or "115" or "882" or "1482" or "393" or "2537" or "532" or "827")
                {
                    #region Нестандартные титлы
                    name = Regex.Match(title, "^([^/\\(\\[]+) ").Groups[1].Value;

                    if (int.TryParse(Regex.Match(title, " \\[([0-9]{4})(,|-) ").Groups[1].Value, out int _yer))
                        relased = _yer;

                    if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        continue;
                    #endregion
                }
                #endregion

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    #region types
                    string[] types = null;
                    switch (cat)
                    {
                        case "549":
                        case "22":
                        case "1666":
                        case "941":
                        case "1950":
                        case "2090":
                        case "2221":
                        case "2091":
                        case "2092":
                        case "2093":
                        case "2200":
                        case "2540":
                        case "934":
                        case "505":
                        case "124":
                        case "1457":
                        case "2199":
                        case "313":
                        case "312":
                        case "1247":
                        case "2201":
                        case "2339":
                        case "140":
                        case "252":
                            types = new string[] { "movie" };
                            break;
                        case "2343":
                        case "930":
                        case "2365":
                        case "208":
                        case "539":
                        case "209":
                        case "1213":
                            types = new string[] { "multfilm" };
                            break;
                        case "921":
                        case "815":
                        case "1460":
                            types = new string[] { "multserial" };
                            break;
                        case "842":
                        case "235":
                        case "242":
                        case "819":
                        case "1531":
                        case "721":
                        case "1102":
                        case "1120":
                        case "1214":
                        case "489":
                        case "387":
                        case "9":
                        case "81":
                        case "119":
                        case "1803":
                        case "266":
                        case "193":
                        case "1690":
                        case "1459":
                        case "825":
                        case "1248":
                        case "1288":
                        case "325":
                        case "534":
                        case "694":
                        case "704":
                        case "915":
                        case "1939":
                            types = new string[] { "serial" };
                            break;
                        case "1105":
                        case "2491":
                        case "1389":
                            types = new string[] { "anime" };
                            break;
                        case "709":
                        case "2109":
                            types = new string[] { "documovie" };
                            break;
                        case "46":
                        case "671":
                        case "2177":
                        case "2538":
                        case "251":
                        case "98":
                        case "97":
                        case "851":
                        case "2178":
                        case "821":
                        case "2076":
                        case "56":
                        case "2123":
                        case "876":
                        case "2139":
                        case "1467":
                        case "1469":
                        case "249":
                        case "552":
                        case "500":
                        case "2112":
                        case "1327":
                        case "1468":
                        case "2168":
                        case "2160":
                        case "314":
                        case "1281":
                        case "2110":
                        case "979":
                        case "2169":
                        case "2164":
                        case "2166":
                        case "2163":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                        case "24":
                        case "1959":
                        case "939":
                        case "1481":
                        case "113":
                        case "115":
                        case "882":
                        case "1482":
                        case "393":
                        case "2537":
                        case "532":
                        case "827":
                            types = new string[] { "tvshow" };
                            break;
                        case "2103":
                        case "2522":
                        case "2485":
                        case "2486":
                        case "2479":
                        case "2089":
                        case "1794":
                        case "845":
                        case "2312":
                        case "343":
                        case "2111":
                        case "1527":
                        case "2069":
                        case "1323":
                        case "2009":
                        case "2000":
                        case "2010":
                        case "2006":
                        case "2007":
                        case "2005":
                        case "259":
                        case "2004":
                        case "1999":
                        case "2001":
                        case "2002":
                        case "283":
                        case "1997":
                        case "2003":
                        case "1608":
                        case "1609":
                        case "2294":
                        case "1229":
                        case "1693":
                        case "2532":
                        case "136":
                        case "592":
                        case "2533":
                        case "1952":
                        case "1621":
                        case "2075":
                        case "1668":
                        case "1613":
                        case "1614":
                        case "1623":
                        case "1615":
                        case "1630":
                        case "2425":
                        case "2514":
                        case "1616":
                        case "2014":
                        case "1442":
                        case "1491":
                        case "1987":
                        case "1617":
                        case "1620":
                        case "1998":
                        case "1343":
                        case "751":
                        case "1697":
                        case "255":
                        case "260":
                        case "261":
                        case "256":
                        case "1986":
                        case "660":
                        case "1551":
                        case "626":
                        case "262":
                        case "1326":
                        case "978":
                        case "1287":
                        case "1188":
                        case "1667":
                        case "1675":
                        case "257":
                        case "875":
                        case "263":
                        case "2073":
                        case "550":
                        case "2124":
                        case "1470":
                        case "528":
                        case "486":
                        case "854":
                        case "2079":
                        case "1336":
                        case "2171":
                        case "1339":
                        case "2455":
                        case "1434":
                        case "2350":
                        case "1472":
                        case "2068":
                        case "2016":
                            types = new string[] { "sport" };
                            break;
                    }

                    if (types == null)
                        continue;
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "rutracker",
                        types = types,
                        url = url,
                        title = title,
                        sid = sid,
                        pir = pir,
                        sizeName = sizeName,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                var fullNews = await HttpClient.Get(t.url, useproxy: AppInit.conf.Rutracker.useproxy);
                if (fullNews != null)
                {
                    string time = Regex.Match(fullNews, "<a class=\"p-link small\" href=\"viewtopic.php\\?t=[^\"]+\">([^<]+)</a>").Groups[1].Value;
                    DateTime createTime = tParse.ParseCreateTime(time.Replace("-", " "), "dd.MM.yy HH:mm");
                    if (createTime != default)
                        t.createTime = createTime;

                    string magnet = Regex.Match(fullNews, "href=\"(magnet:[^\"]+)\" class=\"(med )?magnet-link\"").Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(magnet))
                    {
                        t.magnet = magnet;
                        return true;
                    }
                }

                return false;
            });

            return torrents.Count > 0;
        }
        #endregion
    }
}
