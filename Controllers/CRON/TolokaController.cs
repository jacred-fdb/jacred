using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using JacRed.Engine.CORE;
using JacRed.Models.tParse;
using IO = System.IO;
using JacRed.Engine;
using JacRed.Models.Details;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/toloka/[action]")]
    public class TolokaController : BaseController
    {
        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static TolokaController()
        {
            if (IO.File.Exists("Data/temp/toloka_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/toloka_taskParse.json"));
        }

        #region Cookie / TakeLogin
        static string Cookie(IMemoryCache memoryCache)
        {
            if (memoryCache.TryGetValue("cron:TolokaController:Cookie", out string cookie))
                return cookie;

            return null;
        }

        async static Task<bool> TakeLogin(IMemoryCache memoryCache)
        {
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
                        { "username", AppInit.conf.Toloka.login.u },
                        { "password", AppInit.conf.Toloka.login.p },
                        { "autologin", "on" },
                        { "ssl", "on" },
                        { "redirect", "index.php?" },
                        { "login", "Вхід" }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{AppInit.conf.Toloka.host}/login.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string toloka_sid = null, toloka_data = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("toloka_sid="))
                                        toloka_sid = new Regex("toloka_sid=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("toloka_data="))
                                        toloka_data = new Regex("toloka_data=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(toloka_sid) && !string.IsNullOrWhiteSpace(toloka_data))
                                {
                                    memoryCache.Set("cron:TolokaController:Cookie", $"toloka_sid={toloka_sid}; toloka_ssl=1; toloka_data={toloka_data};", DateTime.Now.AddHours(1));
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
                string baseUrl = AppInit.conf.Toloka.host;
                ParserLog.Write("toloka", $"Starting parse page={page}, base: {baseUrl}");
                foreach (string cat in new List<string>() { "16", "96", "19", "139", "32", "173", "174", "44" })
                {
                    string pageUrl = page == 0 ? $"{baseUrl}/f{cat}" : $"{baseUrl}/f{cat}-{page * 45}?sort=8";
                    ParserLog.Write("toloka", $"Category {cat}: {pageUrl}");
                    await parsePage(cat, page);
                    log += $"{cat} - {page}\n";
                }
                ParserLog.Write("toloka", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("toloka", $"Error: {ex.Message}");
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
            #region Авторизация
            if (Cookie(memoryCache) == null)
            {
                string authKey = "toloka:TakeLogin()";
                if (memoryCache.TryGetValue(authKey, out _))
                {
                    IO.File.WriteAllText("Data/temp/toloka_taskParse.json", JsonConvert.SerializeObject(taskParse));
                    return "TakeLogin == null";
                }

                if (await TakeLogin(memoryCache) == false)
                {
                    memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(5));
                    IO.File.WriteAllText("Data/temp/toloka_taskParse.json", JsonConvert.SerializeObject(taskParse));
                    return "TakeLogin == null";
                }
            }
            #endregion

            foreach (string cat in new List<string>()
            {
                // Українське озвучення
                "16", "32",  "19", "44", "127",

                // Українське кіно
                "84", "42", "124", "125",

                // HD українською
                "96", "173", "139", "174", "140",

                // Документальні фільми українською
                "12", "131", "230", "226", "227", "228", "229",

                // Телевізійні шоу та програми
                "132"
            })
            {
                // Получаем html
                string html = await HttpClient.Get($"{AppInit.conf.Toloka.host}/f{cat}", timeoutSeconds: 10, cookie: Cookie(memoryCache));
                if (html == null)
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, ">([0-9]+)</a>&nbsp;&nbsp;<a href=\"[^\"]+\">наступна</a>").Groups[1].Value, out int maxpages);

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

            IO.File.WriteAllText("Data/temp/toloka_taskParse.json", JsonConvert.SerializeObject(taskParse));
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
                        if (DateTime.Today == val.updateTime)
                            continue;

                        await Task.Delay(AppInit.conf.Toloka.parseDelay);

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
        static bool _parseLatestWork = false;

        async public Task<string> ParseLatest(int pages = 5)
        {
            if (_parseLatestWork)
                return "work";

            _parseLatestWork = true;
            string log = "";

            try
            {
                var sw = Stopwatch.StartNew();
                ParserLog.Write("toloka", $"Starting ParseLatest pages={pages}");

                foreach (var task in taskParse.ToArray())
                {
                    // Get first N pages sorted by page number
                    var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();

                    foreach (var val in pagesToParse)
                    {
                        await Task.Delay(AppInit.conf.Toloka.parseDelay);

                        bool res = await parsePage(task.Key, val.page);
                        if (res)
                        {
                            val.updateTime = DateTime.Today;
                            log += $"{task.Key} - {val.page}\n";
                        }
                    }
                }

                ParserLog.Write("toloka", $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("toloka", $"ParseLatest Error: {ex.Message}");
            }

            _parseLatestWork = false;
            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(string cat, int page)
        {
            #region Авторизация
            if (Cookie(memoryCache) == null)
            {
                string authKey = "toloka:TakeLogin()";
                if (memoryCache.TryGetValue(authKey, out _))
                    return false;

                if (await TakeLogin(memoryCache) == false)
                {
                    memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(5));
                    return false;
                }
            }
            #endregion

            string html = await HttpClient.Get($"{AppInit.conf.Toloka.host}/f{cat}{(page == 0 ? "" : $"-{page * 45}")}?sort=8", cookie: Cookie(memoryCache)/*, useproxy: true, proxy: tParse.webProxy()*/);
            if (html == null || !html.Contains("<html lang=\"uk\""))
                return false;

            var torrents = new List<TolokaDetails>();

            foreach (string row in tParse.ReplaceBadNames(html).Split("</tr>").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row) || Regex.IsMatch(row, "Збір коштів", RegexOptions.IgnoreCase))
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
                string _createTime = Match("class=\"postdetails\">([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})").Replace("-", ".");
                if (!DateTime.TryParse(_createTime, out DateTime createTime) || createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("<a href=\"(t[0-9]+)\" class=\"topictitle\"");
                string title = Match("class=\"topictitle\">([^<]+)</a>");

                string _sid = Match("<span class=\"seedmed\" [^>]+><b>([0-9]+)</b></span>");
                string _pir = Match("<span class=\"leechmed\" [^>]+><b>([0-9]+)</b></span>");
                string sizeName = Match("<a href=\"download.php[^\"]+\" [^>]+>([^<]+)</a>").Replace("&nbsp;", " ");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName) || sizeName == "0 B")
                    continue;

                url = $"{AppInit.conf.Toloka.host}/{url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat is "16" or "96" or "19" or "139" or "12" or "131" or "84" or "42")
                {
                    #region Фильмы
                    // Незворотність / Irréversible / Irreversible (2002) AVC Ukr/Fre | Sub Eng
                    var g = Regex.Match(title, "^([^/\\(\\[]+)/[^/\\(\\[]+/([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value.Trim();
                        originalname = g[2].Value.Trim();

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Мій рік у Нью-Йорку / My Salinger Year (2020) Ukr/Eng
                        g = Regex.Match(title, "^([^/\\(\\[]+)/([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value.Trim();
                            originalname = g[2].Value.Trim();

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Хроніка надій та ілюзій. Дзеркало історії. (83 серії) (2001-2003) PDTVRip
                            g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                            {
                                name = g[1].Value;

                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Берестечко. Битва за Україну (2015-2016) DVDRip-AVC
                                g = Regex.Match(title, "^([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                {
                                    name = g[1].Value;

                                    if (int.TryParse(g[2].Value, out int _yer))
                                        relased = _yer;
                                }
                            }
                        }
                    }
                    #endregion
                }
                else if (cat is "32" or "173" or "174" or "44" or "230" or "226" or "227" or "228" or "229" or "127" or "124" or "125" or "132")
                {
                    #region Сериалы
                    // Атака титанів (Attack on Titan) (Сезон 1) / Shingeki no Kyojin (Season 1) (2013) BDRip 720р
                    var g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\([^\\)]+\\) ?/([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value.Trim();
                        originalname = g[2].Value.Trim();

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Дім з прислугою (Сезон 2, серії 1-8) / Servant (Season 2, episodes 1-8) (2021) WEB-DLRip-AVC Ukr/Eng
                        g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) ?/([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value.Trim();
                            originalname = g[2].Value.Trim();

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Детективне агентство прекрасних хлопчиків (08 з 12) / Bishounen Tanteidan (2021) BDRip 1080p Ukr/Jap | Ukr Sub
                            g = Regex.Match(title, "^([^/\\(\\[]+) (\\(|\\[)[^\\)\\]]+(\\)|\\]) ?/([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                            {
                                name = g[1].Value.Trim();
                                originalname = g[4].Value.Trim();

                                if (int.TryParse(g[5].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Яйця Дракона / Dragon Ball (01-31 з 153) (1986-1989) BDRip 1080p H.265
                                // Томо — дівчина! / Tomo-chan wa Onnanoko! (Сезон 1, серії 01-02 з 13) (2023) WEBDL 1080p H.265 Ukr/Jap | sub Ukr
                                g = Regex.Match(title, "^([^/\\(\\[]+)/([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                {
                                    name = g[1].Value.Trim();
                                    originalname = g[2].Value.Trim();

                                    if (int.TryParse(g[3].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // Людина-бензопила / チェンソーマン /Chainsaw Man (сезон 1, серії 8 з 12) (2022) WEBRip 1080p
                                    g = Regex.Match(title, "^([^/\\(\\[]+)/[^/\\(\\[]+/([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                    {
                                        name = g[1].Value.Trim();
                                        originalname = g[2].Value.Trim();

                                        if (int.TryParse(g[3].Value, out int _yer))
                                            relased = _yer;
                                    }
                                    else
                                    {
                                        // МастерШеф. 10 сезон (1-18 епізоди) (2020) IPTVRip 400p
                                        g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                        {
                                            name = g[1].Value.Trim();

                                            if (int.TryParse(g[2].Value, out int _yer))
                                                relased = _yer;
                                        }
                                    }
                                }
                            }
                        }
                    }
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
                        case "16":
                        case "96":
                        case "42":
                            types = new string[] { "movie" };
                            break;
                        case "19":
                        case "139":
                        case "84":
                            types = new string[] { "multfilm" };
                            break;
                        case "32":
                        case "173":
                        case "124":
                            types = new string[] { "serial" };
                            break;
                        case "174":
                        case "44":
                        case "125":
                            types = new string[] { "multserial" };
                            break;
                        case "226":
                        case "227":
                        case "228":
                        case "229":
                        case "230":
                        case "12":
                        case "131":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                        case "127":
                            types = new string[] { "anime" };
                            break;
                        case "132":
                            types = new string[] { "tvshow" };
                            break;
                    }

                    if (types == null)
                        continue;
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    string downloadId = Regex.Match(row, "href=\"download.php\\?id=([0-9]+)\"").Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(downloadId))
                        continue;

                    torrents.Add(new TolokaDetails()
                    {
                        trackerName = "toloka",
                        types = types,
                        url = url,
                        title = title,
                        sid = sid,
                        pir = pir,
                        sizeName = sizeName,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased,
                        downloadId = downloadId
                    });
                }
            }

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                byte[] torrent = await HttpClient.Download($"{AppInit.conf.Toloka.host}/download.php?id={t.downloadId}", cookie: Cookie(memoryCache), referer: AppInit.conf.Toloka.host);
                string magnet = BencodeTo.Magnet(torrent);
                if (magnet != null)
                {
                    t.magnet = magnet;
                    return true;
                }

                return false;
            });

            return torrents.Count > 0;
        }
        #endregion
    }
}
