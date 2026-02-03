using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
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
    [Route("/cron/selezen/[action]")]
    public class SelezenController : BaseController
    {
        static List<TaskParse> taskParse = new List<TaskParse>();

        static SelezenController()
        {
            if (IO.File.Exists("Data/temp/selezen_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<List<TaskParse>>(IO.File.ReadAllText("Data/temp/selezen_taskParse.json"));
        }

        #region Cookie / TakeLogin
        static string Cookie(IMemoryCache memoryCache)
        {
            if (memoryCache.TryGetValue("selezen:cookie", out string cookie))
                return cookie;

            return null;
        }

        async Task<bool> TakeLogin()
        {
            string authKey = "selezen:TakeLogin()";
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
                        { "login_name", AppInit.conf.Selezen.login.u },
                        { "login_password", AppInit.conf.Selezen.login.p },
                        { "login_not_save", "1" },
                        { "login", "submit" }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync(AppInit.conf.Selezen.host, postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string PHPSESSID = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("PHPSESSID="))
                                        PHPSESSID = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(PHPSESSID))
                                {
                                    memoryCache.Set("selezen:cookie", $"PHPSESSID={PHPSESSID}; _ym_isad=2;", DateTime.Now.AddDays(1));
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

        async public Task<string> Parse(int page = 1)
        {
            if (_workParse)
                return "work";

            _workParse = true;

            try
            {
                var sw = Stopwatch.StartNew();
                string pageUrl = page == 1
                    ? $"{AppInit.conf.Selezen.host}/relizy-ot-selezen/"
                    : $"{AppInit.conf.Selezen.host}/relizy-ot-selezen/page/{page}/";
                ParserLog.Write("selezen", $"Starting parse page={page}: {pageUrl}");
                await parsePage(page);
                ParserLog.Write("selezen", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("selezen", $"Error: {ex.Message}");
            }
            finally
            {
                _workParse = false;
            }

            return "ok";
        }
        #endregion

        #region UpdateTasksParse
        async public Task<string> UpdateTasksParse()
        {
            // Получаем html
            string html = await HttpClient.Get($"{AppInit.conf.Selezen.host}/relizy-ot-selezen/", timeoutSeconds: 10, useproxy: AppInit.conf.Selezen.useproxy);
            if (html == null)
                return "html == null";

            // Максимальное количиство страниц
            int.TryParse(Regex.Match(html, "<span class='page-link'>...</span></li> <li class='page-item'><a class='page-link' href=\"[^\"]+/page/[0-9]+/\">([0-9]+)</a></li>").Groups[1].Value, out int maxpages);

            if (maxpages == 0)
                maxpages = 1;

            // Загружаем список страниц в список задач
            for (int page = 1; page <= maxpages; page++)
            {
                try
                {
                    if (taskParse.FirstOrDefault(i => i.page == page) == null)
                        taskParse.Add(new TaskParse(page));
                }
                catch { }
            }

            IO.File.WriteAllText("Data/temp/selezen_taskParse.json", JsonConvert.SerializeObject(taskParse));
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

            foreach (var val in taskParse.ToArray())
            {
                try
                {
                    if (DateTime.Today == val.updateTime)
                        continue;

                    await Task.Delay(AppInit.conf.Selezen.parseDelay);

                    bool res = await parsePage(val.page);
                    if (res)
                        val.updateTime = DateTime.Today;
                }
                catch { }
            }

            _parseAllTaskWork = false;
            return "ok";
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(int page)
        {
            #region Авторизация
            if (Cookie(memoryCache) == null && string.IsNullOrEmpty(AppInit.conf.Selezen.cookie))
            {
                if (await TakeLogin() == false)
                    return false;
            }
            #endregion

            string cookie = AppInit.conf.Selezen.cookie ?? Cookie(memoryCache);
            string html = await HttpClient.Get(page == 1 ? $"{AppInit.conf.Selezen.host}/relizy-ot-selezen/" : $"{AppInit.conf.Selezen.host}/relizy-ot-selezen/page/{page}/", cookie: cookie, useproxy: AppInit.conf.Selezen.useproxy);
            if (html == null || !html.Contains("dle_root"))
                return false;

            if (!html.Contains($">{AppInit.conf.Selezen.login.u}<"))
            {
                if (string.IsNullOrEmpty(AppInit.conf.Selezen.cookie))
                    await TakeLogin();

                return false;
            }

            var torrents = new List<TorrentBaseDetails>();

            foreach (string row in tParse.ReplaceBadNames(html).Split("card overflow-hidden").Skip(1))
            {
                if (row.Contains(">Аниме</a>") || row.Contains(" [S0"))
                    continue;

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
                DateTime createTime = tParse.ParseCreateTime(Match("class=\"bx bx-calendar\"></span> ?([0-9]{2}\\.[0-9]{2}\\.[0-9]{4} [0-9]{2}:[0-9]{2})</a>"), "dd.MM.yyyy HH:mm");

                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                var g = Regex.Match(row, "<a href=\"(https?://[^<]+)\"><h4 class=\"card-title\">([^<]+)</h4>").Groups;
                string url = g[1].Value;
                string title = g[2].Value;

                string _sid = Match("<i class=\"bx bx-chevrons-up\"></i>([0-9 ]+)").Trim();
                string _pir = Match("<i class=\"bx bx-chevrons-down\"></i>([0-9 ]+)").Trim();
                string sizeName = Match("<span class=\"bx bx-download\"></span>([^<]+)</a>").Trim();

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                // Бэд трип / Приколисты в дороге / Bad Trip (2020)
                g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
                else
                {
                    // Летний лагерь / A Week Away (2021)
                    g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
                #endregion

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    #region types
                    string[] types = new string[] { "movie" };
                    if (row.Contains(">Мульт") || row.Contains(">мульт"))
                        types = new string[] { "multfilm" };
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "selezen",
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

                string fullnews = await HttpClient.Get(t.url, cookie: cookie, useproxy: AppInit.conf.Selezen.useproxy);
                if (fullnews != null)
                {
                    string _mg = Regex.Match(fullnews, "href=\"(magnet:\\?xt=urn:btih:[^\"]+)\"").Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(_mg))
                    {
                        t.magnet = _mg;
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
