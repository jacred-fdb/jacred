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
    [Route("/cron/kinozal/[action]")]
    public class KinozalController : BaseController
    {
        static Dictionary<string, Dictionary<string, List<TaskParse>>> taskParse = new Dictionary<string, Dictionary<string, List<TaskParse>>>();

        static KinozalController()
        {
            if (IO.File.Exists("Data/temp/kinozal_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<TaskParse>>>>(IO.File.ReadAllText("Data/temp/kinozal_taskParse.json"));
        }

        #region Cookie / TakeLogin
        static string Cookie;

        async void TakeLogin()
        {
            string authKey = "kinozal:TakeLogin()";
            if (memoryCache.TryGetValue(authKey, out _))
                return;

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
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/99.0.4844.51 Safari/537.36");
                    client.DefaultRequestHeaders.Add("cache-control", "no-cache");
                    client.DefaultRequestHeaders.Add("dnt", "1");
                    client.DefaultRequestHeaders.Add("origin", AppInit.conf.Kinozal.host);
                    client.DefaultRequestHeaders.Add("pragma", "no-cache");
                    client.DefaultRequestHeaders.Add("referer", $"{AppInit.conf.Kinozal.host}/");
                    client.DefaultRequestHeaders.Add("upgrade-insecure-requests", "1");

                    var postParams = new Dictionary<string, string>
                    {
                        { "username", AppInit.conf.Kinozal.login.u },
                        { "password", AppInit.conf.Kinozal.login.p },
                        { "returnto", "" }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{AppInit.conf.Kinozal.host}/takelogin.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string uid = null, pass = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("uid="))
                                        uid = new Regex("uid=([0-9]+)").Match(line).Groups[1].Value;

                                    if (line.Contains("pass="))
                                        pass = new Regex("pass=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(pass))
                                    Cookie = $"uid={uid}; pass={pass};";
                            }
                        }
                    }
                }
            }
            catch { }
        }
        #endregion


        #region Parse
        static bool _workParse = false;

        async public Task<string> Parse(int page)
        {
            if (_workParse)
                return "work";

            string log = "";
            _workParse = true;

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = $"{AppInit.conf.Kinozal.host}/browse.php";
                ParserLog.Write("kinozal", $"Starting parse page={page}, base: {baseUrl}");
                foreach (string cat in new List<string>()
                {
                    // Сериалы
                    "45", "46",

                    // Фильмы
                    "8", "6", "15", "17", "35", "39", "13", "14", "24", "11", "9", "47", "18", "37", "12", "10", "7", "16",

                    // ТВ-шоу
                    "49", "50",

                    // Мульты
                    "21", "22",

                    // Аниме
                    "20"
                })
                {
                    string pageUrl = $"{baseUrl}?c={cat}&page={page}";
                    ParserLog.Write("kinozal", $"Category {cat}: {pageUrl}");
                    await parsePage(cat, page);
                    log += $"{cat} - {page}\n";
                }
                ParserLog.Write("kinozal", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("kinozal", $"Error: {ex.Message}");
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
            foreach (string cat in new List<string>()
            {
                // Сериалы
                "45", "46",

                // Фильмы
                "8", "6", "15", "17", "35", "39", "13", "14", "24", "11", "9", "47", "18", "37", "12", "10", "7", "16",

                // ТВ-шоу
                "49", "50",

                // Мульты
                "21", "22",

                // Аниме
                "20"
            })
            {
                for (int year = DateTime.Today.Year; year >= 1990; year--)
                {
                    // Получаем html
                    string html = await HttpClient.Get($"{AppInit.conf.Kinozal.host}/browse.php?c={cat}&d={year}&t=1", timeoutSeconds: 10, useproxy: AppInit.conf.Kinozal.useproxy);
                    if (html == null)
                        continue;

                    // Максимальное количиство страниц
                    int.TryParse(Regex.Match(html, ">([0-9]+)</a></li><li><a rel=\"next\"").Groups[1].Value, out int maxpages);

                    // Загружаем список страниц в список задач
                    for (int page = 0; page <= maxpages; page++)
                    {
                        try
                        {
                            if (!taskParse.ContainsKey(cat))
                                taskParse.Add(cat, new Dictionary<string, List<TaskParse>>());

                            string arg = $"&d={year}&t=1";
                            var catVal = taskParse[cat];
                            if (!catVal.ContainsKey(arg))
                                catVal.Add(arg, new List<TaskParse>());

                            var val = catVal[arg];
                            if (val.FirstOrDefault(i => i.page == page) == null)
                                val.Add(new TaskParse(page));
                        }
                        catch { }
                    }
                }
            }

            IO.File.WriteAllText("Data/temp/kinozal_taskParse.json", JsonConvert.SerializeObject(taskParse));
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
                foreach (var cat in taskParse.ToArray())
                {
                    foreach (var arg in cat.Value.ToArray())
                    {
                        foreach (var val in arg.Value.ToArray())
                        {
                            if (DateTime.Today == val.updateTime)
                                continue;

                            await Task.Delay(AppInit.conf.Kinozal.parseDelay);

                            bool res = await parsePage(cat.Key, val.page, arg.Key);
                            if (res)
                                val.updateTime = DateTime.Today;
                        }
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
                ParserLog.Write("kinozal", $"Starting ParseLatest pages={pages}");

                foreach (var cat in taskParse.ToArray())
                {
                    foreach (var arg in cat.Value.ToArray())
                    {
                        // Get first N pages sorted by page number
                        var pagesToParse = arg.Value.OrderBy(x => x.page).Take(pages).ToArray();

                        foreach (var val in pagesToParse)
                        {
                            await Task.Delay(AppInit.conf.Kinozal.parseDelay);

                            bool res = await parsePage(cat.Key, val.page, arg.Key);
                            if (res)
                            {
                                val.updateTime = DateTime.Today;
                                log += $"{cat.Key} - {arg.Key} - {val.page}\n";
                            }
                        }
                    }
                }

                ParserLog.Write("kinozal", $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("kinozal", $"ParseLatest Error: {ex.Message}");
            }
            finally
            {
                _parseLatestSemaphore.Release();
            }

            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(string cat, int page, string arg = null)
        {
            string html = await HttpClient.Get($"{AppInit.conf.Kinozal.host}/browse.php?c={cat}&page={page}" + arg, useproxy: AppInit.conf.Kinozal.useproxy);
            if (html == null || !html.Contains("Кинозал.ТВ</title>"))
                return false;

            if (Cookie == null || !html.Contains(">Выход</a>"))
                TakeLogin();

            var torrents = new List<TorrentBaseDetails>();

            foreach (string row in Regex.Split(tParse.ReplaceBadNames(html), "<tr class=('first bg'|bg)>").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Дата создания
                DateTime createTime = default;

                if (row.Contains("<td class='s'>сегодня"))
                {
                    createTime = DateTime.UtcNow;
                }
                else if (row.Contains("<td class='s'>вчера"))
                {
                    createTime = DateTime.UtcNow.AddDays(-1);
                }
                else
                {
                    createTime = tParse.ParseCreateTime(Match("<td class='s'>([0-9]{2}.[0-9]{2}.[0-9]{4}) в [0-9]{2}:[0-9]{2}</td>"), "dd.MM.yyyy");
                }

                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("href=\"/(details.php\\?id=[0-9]+)\"");
                string title = Match("class=\"r[0-9]+\">([^<]+)</a>");
                string _sid = Match("<td class='sl_s'>([0-9]+)</td>");
                string _pir = Match("<td class='sl_p'>([0-9]+)</td>");
                string sizeName = Match("<td class='s'>([0-9\\.,]+ (МБ|ГБ))</td>");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                url = "http://kinozal.tv/" + url;
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat is "8" or "6" or "15" or "17" or "35" or "39" or "13" or "14" or "24" or "11" or "9" or "47" or "18" or "37" or "12" or "10" or "7" or "16")
                {
                    #region Фильмы
                    // Бэд трип (Приколисты в дороге) / Bad Trip / 2020 / ДБ, СТ / WEB-DLRip (AVC)
                    // Интерстеллар / Interstellar (IMAX Edition) / 2014 / ДБ / BDRip
                    // Успеть всё за месяц / 30 jours max / 2020 / ЛМ / WEB-DLRip
                    var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?/ ([^\\(/]+) (\\([^\\)/]+\\) )?/ ([0-9]{4})").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[3].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[5].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Голая правда / 2020 / ЛМ / WEB-DLRip
                        g = Regex.Match(title, "^([^/\\(]+) / ([0-9]{4})").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[2].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                else if (cat == "45" || cat == "22")
                {
                    #region Сериал - Русский
                    if (row.Contains("сезон"))
                    {
                        // Сельский детектив (6 сезон: 1-2 серии из 2) ([^/]+)?/ 2020 / РУ / WEB-DLRip (AVC)
                        // Любовь в рабочие недели (1 сезон: 1 серия из 15) / 2020 / РУ / WEB-DLRip (AVC)
                        // Фитнес (Королева фитнеса) (1-4 сезон: 1-80 серии из 80) / 2018-2020 / РУ / WEB-DLRip
                        // Бывшие (1-3 сезон: 1-24 серии из 24) / 2016-2020 / РУ / WEB-DLRip (AVC)
                        var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([0-9\\-]+ сезоны?: [^\\)/]+\\) ([^/]+ )?/ ([0-9]{4})").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value))
                        {
                            name = g[1].Value;

                            if (int.TryParse(g[4].Value, out int _yer))
                                relased = _yer;
                        }
                    }
                    else
                    {
                        // Авантюра на двоих (1-8 серии из 8) / 2021 / РУ /  WEBRip (AVC)
                        // Жизнь после жизни (Небеса подождут) (1-16 серии из 16) / 2016 / РУ / WEB-DLRip
                        var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([^\\)/]+\\) ([^/]+ )?/ ([0-9]{4})").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                else if (cat == "46" || cat == "21" || cat == "20")
                {
                    #region Сериал - Буржуйский
                    if (row.Contains("сезон"))
                    {
                        // Сокол и Зимний солдат (1 сезон: 1-2 серия из 6) / The Falcon and the Winter Soldier / 2021 / ЛД (#NW), СТ / WEB-DL (1080p)
                        // Голубая кровь (Семейная традиция) (11 сезон: 1-9 серия из 20) / Blue Bloods / 2020 / ПМ (BaibaKo) / WEBRip
                        var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([0-9\\-]+ сезоны?: [^\\)/]+\\) ([^/]+ )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                        {
                            name = g[1].Value;
                            originalname = g[4].Value;

                            if (int.TryParse(g[5].Value, out int _yer))
                                relased = _yer;
                        }
                    }
                    else
                    {
                        // Дикий ангел (151-270 серии из 270) / Muneca Brava / 1998-1999 / ПМ / DVB
                        var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([^\\)/]+\\) ([^/]+ )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                        {
                            name = g[1].Value;
                            originalname = g[4].Value;

                            if (int.TryParse(g[5].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            g = Regex.Match(title, "^([^\\(/]+) / ([^\\(/]+) / ([0-9]{4})").Groups;
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                    }
                    #endregion
                }
                else if (cat == "49" || cat == "50")
                {
                    #region ТВ-шоу
                    // Топ Гир (30 сезон: 1-2 выпуски из 10) / Top Gear / 2021 / ЛМ (ColdFilm) / WEBRip
                    var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[3].Value) && !string.IsNullOrWhiteSpace(g[4].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Супермама (3 сезон: 1-12 выпуски из 40) / 2021 / РУ / IPTV (1080p)
                        g = Regex.Match(title, "^([^/\\(]+) (\\([^\\)/]+\\) )?/ ([0-9]{4})").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
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
                        case "8":
                        case "6":
                        case "15":
                        case "17":
                        case "35":
                        case "39":
                        case "13":
                        case "14":
                        case "24":
                        case "11":
                        case "9":
                        case "47":
                        case "18":
                        case "37":
                        case "12":
                        case "10":
                        case "7":
                        case "16":
                            types = new string[] { "movie" };
                            break;
                        case "45":
                        case "46":
                            types = new string[] { "serial" };
                            break;
                        case "49":
                        case "50":
                            types = new string[] { "tvshow" };
                            break;
                        case "21":
                        case "22":
                            types = new string[] { "multfilm", "multserial" };
                            break;
                        case "20":
                            types = new string[] { "anime" };
                            break;
                    }

                    if (types == null)
                        continue;
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "kinozal",
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

                string id = Regex.Match(t.url, "\\?id=([0-9]+)").Groups[1].Value;
                string srv_details = await HttpClient.Post($"{AppInit.conf.Kinozal.host}/get_srv_details.php?id={id}&action=2", $"id={id}&action=2", Cookie, useproxy: AppInit.conf.Kinozal.useproxy);
                if (srv_details != null)
                {
                    string torrentHash = new Regex("<ul><li>Инфо хеш: +([^<]+)</li>").Match(srv_details).Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(torrentHash))
                    {
                        t.magnet = $"magnet:?xt=urn:btih:{torrentHash}";
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
