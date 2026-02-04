using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using JacRed.Engine.CORE;
using JacRed.Models.tParse;
using IO = System.IO;
using JacRed.Engine;
using System.Text;
using JacRed.Models.Details;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/megapeer/[action]")]
    public class MegapeerController : BaseController
    {
        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static MegapeerController()
        {
            if (IO.File.Exists("Data/temp/megapeer_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/megapeer_taskParse.json"));
        }

        /// <summary>Маркер валидной страницы browse (при запросе через alias/worker может прийти 200 с телом ошибки).</summary>
        const string BrowsePageValidMarker = "id=\"logo\"";

        /// <summary>Запрос страницы browse с повтором при rate limit. Успех только по контенту: при alias (Cloudflare Worker) часто приходит 200 с телом ошибки, а не 429.</summary>
        async Task<string> GetMegapeerBrowsePage(string url, string cat)
        {
            var headers = new List<(string name, string val)>()
            {
                ("dnt", "1"),
                ("pragma", "no-cache"),
                ("referer", $"{AppInit.conf.Megapeer.rqHost()}/cat/{cat}"),
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-user", "?1"),
                ("upgrade-insecure-requests", "1")
            };
            const int maxRetries = 3;
            const int defaultWaitSec = 60;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var (content, response) = await HttpClient.BaseGetAsync(url, encoding: Encoding.GetEncoding(1251), useproxy: AppInit.conf.Megapeer.useproxy, addHeaders: headers);

                // Успех только если контент есть и это реальная страница каталога (не страница ошибки/rate limit при 200)
                if (!string.IsNullOrEmpty(content) && content.Contains(BrowsePageValidMarker))
                    return content;

                // Пустой ответ или невалидная страница (в т.ч. 200 с телом "подождите") — считаем rate limit, ждём и повторяем
                int waitSec = defaultWaitSec;
                var status = response?.StatusCode;
                if (status == (HttpStatusCode)429)
                {
                    try
                    {
                        if (response.Headers.RetryAfter?.Delta != null)
                            waitSec = (int)Math.Max(defaultWaitSec, response.Headers.RetryAfter.Delta.Value.TotalSeconds);
                        else if (response.Headers.RetryAfter?.Date != null)
                            waitSec = (int)Math.Max(defaultWaitSec, (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
                    }
                    catch { }
                }
                if (attempt < maxRetries)
                {
                    ParserLog.Write("megapeer", $"Rate limit or invalid page (status={(int)(status ?? 0)}), waiting {waitSec}s before retry {attempt}/{maxRetries}");
                    await Task.Delay(TimeSpan.FromSeconds(waitSec));
                    continue;
                }
                return null;
            }
            return null;
        }

        #region Parse
        static bool _workParse = false;

        async public Task<string> Parse(int page)
        {
            if (AppInit.conf?.disable_trackers != null && AppInit.conf.disable_trackers.Contains("megapeer", StringComparer.OrdinalIgnoreCase))
                return "disabled";
            if (_workParse)
                return "work";

            _workParse = true;
            string log = "";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = $"{AppInit.conf.Megapeer.rqHost()}/browse.php";
                ParserLog.Write("megapeer", $"Starting parse page={page}, base: {baseUrl}");
                // 80 - Зарубежные фильмы          | Фильмы
                // 79  - Наши фильмы                | Фильмы
                // 6   - Зарубежные сериалы         | Сериалы
                // 5   - Наши сериалы               | Сериалы
                // 55  - Научно-популярные фильмы   | Док. сериалы, Док. фильмы
                // 57  - Телевизор                  | ТВ Шоу
                // 76  - Мультипликация             | Мультфильмы, Мультсериалы
                foreach (string cat in new List<string>() { "80", "79", "6", "5", "55", "57", "76" })
                {
                    await Task.Delay(AppInit.conf.Megapeer.parseDelay);
                    string pageUrl = $"{baseUrl}?cat={cat}&page={page}";
                    ParserLog.Write("megapeer", $"Category {cat}: {pageUrl}");
                    bool res = await parsePage(cat, page);
                    log += $"{cat} - {page} / {res}\n";
                }
                ParserLog.Write("megapeer", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("megapeer", $"Error: {ex.Message}");
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
            if (AppInit.conf?.disable_trackers != null && AppInit.conf.disable_trackers.Contains("megapeer", StringComparer.OrdinalIgnoreCase))
                return "disabled";
            foreach (string cat in new List<string>() { "80", "79", "6", "5", "55", "57", "76" })
            {
                await Task.Delay(AppInit.conf.Megapeer.parseDelay);
                string html = await GetMegapeerBrowsePage($"{AppInit.conf.Megapeer.rqHost()}/browse.php?cat={cat}", cat);

                if (html == null)
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, ">Всего: ([0-9]+)").Groups[1].Value, out int maxpages);
                maxpages = maxpages / 50;

                if (maxpages > 10)
                    maxpages = 10;

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

            IO.File.WriteAllText("Data/temp/megapeer_taskParse.json", JsonConvert.SerializeObject(taskParse));
            return "ok";
        }
        #endregion

        #region ParseAllTask
        static bool _parseAllTaskWork = false;

        async public Task<string> ParseAllTask()
        {
            if (AppInit.conf?.disable_trackers != null && AppInit.conf.disable_trackers.Contains("megapeer", StringComparer.OrdinalIgnoreCase))
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

                        await Task.Delay(AppInit.conf.Megapeer.parseDelay);

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


        #region parsePage
        async Task<bool> parsePage(string cat, int page)
        {
            string html = await GetMegapeerBrowsePage($"{AppInit.conf.Megapeer.rqHost()}/browse.php?cat={cat}&page={page}", cat);

            if (html == null || !html.Contains(BrowsePageValidMarker))
                return false;

            var torrents = new List<MegapeerDetails>();

            // Структура browse.php?cat=79&page=3: строки по разделителю class="table_fon";
            // в строке: <td>3 янв 26</td><td>...[D](/download/ID)[Название (2025) WEB-DLRip](/torrent/ID/slug)...</td><td align="right">1.72 GB</td>...![S] 8 ![L] 0
            foreach (string row in html.Split("class=\"table_fon\"").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Replace(" ", " ").Trim(); // Меняем непонятный символ похожий на проблел, на обычный проблел
                }
                #endregion

                #region createTime
                DateTime createTime = tParse.ParseCreateTime(Match("<td>([0-9]+ [^ ]+ [0-9]+)</td><td>"), "dd.MM.yy");
                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("href=\"/(torrent/[0-9]+)");
                string title = Match("class=\"url\"[^>]*>([^<]+)</a>", 1);
                if (string.IsNullOrWhiteSpace(title))
                    title = Match("class=\"url\">([^<]+)</a></td>");

                string sizeName = Match("<td align=\"right\">([^<\n\r]+)", 1).Trim();

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                string _sid = Match("alt=\"S\"><font [^>]+>([0-9]+)</font>", 1);
                if (string.IsNullOrWhiteSpace(_sid))
                    _sid = Match("alt=\"S\"[^>]*>\\s*([0-9]+)", 1);
                string _pir = Match("alt=\"L\"><font [^>]+>([0-9]+)</font>", 1);
                if (string.IsNullOrWhiteSpace(_pir))
                    _pir = Match("alt=\"L\"[^>]*>\\s*([0-9]+)", 1);

                url = $"{AppInit.conf.Megapeer.host}/{url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat == "80")
                {
                    #region Зарубежные фильмы
                    var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;

                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                else if (cat == "79")
                {
                    #region Наши фильмы
                    var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;

                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                    #endregion
                }
                else if (cat == "6")
                {
                    #region Зарубежные сериалы
                    var g = Regex.Match(title, "^([^/]+) / [^/]+ / [^/]+ / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        g = Regex.Match(title, "^([^/]+) / [^/]+ / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            g = Regex.Match(title, "^([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;

                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                    }
                    #endregion
                }
                else if (cat == "5")
                {
                    #region Наши сериалы
                    var g = Regex.Match(title, "^([^/]+) \\[[^\\]]+\\] \\(([0-9]{4})(\\)|-)").Groups;
                    name = g[1].Value;

                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                    #endregion
                }
                else if (cat == "55" || cat == "57" || cat == "76")
                {
                    #region Научно-популярные фильмы / Телевизор / Мультипликация
                    if (title.Contains(" / "))
                    {
                        if (title.Contains("[") && title.Contains("]"))
                        {
                            var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[3].Value;

                                if (int.TryParse(g[4].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                g = Regex.Match(title, "^([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;

                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                        else
                        {
                            var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[3].Value;

                                if (int.TryParse(g[4].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;

                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                    }
                    else
                    {
                        if (title.Contains("[") && title.Contains("]"))
                        {
                            var g = Regex.Match(title, "^([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                            name = g[1].Value;

                            if (int.TryParse(g[2].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                            name = g[1].Value;

                            if (int.TryParse(g[2].Value, out int _yer))
                                relased = _yer;
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
                    string[] types = new string[] { };
                    switch (cat)
                    {
                        case "80":
                        case "79":
                            types = new string[] { "movie" };
                            break;
                        case "6":
                        case "5":
                            types = new string[] { "serial" };
                            break;
                        case "55":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                        case "57":
                            types = new string[] { "tvshow" };
                            break;
                        case "76":
                            types = new string[] { "multfilm", "multserial" };
                            break;
                    }
                    #endregion

                    string downloadid = Match("href=\"/?download/([0-9]+)\"");
                    if (string.IsNullOrWhiteSpace(downloadid))
                        continue;

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new MegapeerDetails()
                    {
                        trackerName = "megapeer",
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
                        downloadId = downloadid
                    });
                }
            }

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                byte[] _t = await HttpClient.Download($"{AppInit.conf.Megapeer.host}/download/{t.downloadId}", referer: AppInit.conf.Megapeer.host);
                string magnet = BencodeTo.Magnet(_t);

                if (!string.IsNullOrWhiteSpace(magnet))
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
