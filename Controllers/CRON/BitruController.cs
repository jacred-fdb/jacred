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
using JacRed.Models.Details;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/bitru/[action]")]
    public class BitruController : BaseController
    {
        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static BitruController()
        {
            if (IO.File.Exists("Data/temp/bitru_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/bitru_taskParse.json"));
        }


        #region Parse
        static bool _workParse = false;

        async public Task<string> Parse(int page = 1)
        {
            if (_workParse)
                return "work";

            _workParse = true;
            string log = "";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = $"{AppInit.conf.Bitru.rqHost()}/browse.php";
                ParserLog.Write("bitru", $"Starting parse page={page}, base: {baseUrl}");
                // movie     - Фильмы    | Фильмы
                // serial    - Сериалы   | Сериалы
                foreach (string cat in new List<string>() { "movie", "serial" })
                {
                    string pageUrl = $"{baseUrl}?tmp={cat}&page={page}";
                    ParserLog.Write("bitru", $"Category {cat}: {pageUrl}");
                    await parsePage(cat, page);
                    log += $"{cat} - {page}\n";
                }
                ParserLog.Write("bitru", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("bitru", $"Error: {ex.Message}");
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
            // movie     - Фильмы    | Фильмы
            // serial    - Сериалы   | Сериалы
            foreach (string cat in new List<string>() { "movie", "serial" })
            {
                // Получаем html
                string html = await HttpClient.Get($"{AppInit.conf.Bitru.rqHost()}/browse.php?tmp={cat}", timeoutSeconds: 10, useproxy: AppInit.conf.Bitru.useproxy);
                if (html == null)
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, $"<a href=\"browse.php\\?tmp={cat}&page=[^\"]+\">([0-9]+)</a></div>").Groups[1].Value, out int maxpages);

                if (maxpages == 0)
                    maxpages = 1;

                // Загружаем список страниц в список задач
                for (int page = 1; page <= maxpages; page++)
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

            IO.File.WriteAllText("Data/temp/bitru_taskParse.json", JsonConvert.SerializeObject(taskParse));
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

                        await Task.Delay(AppInit.conf.Bitru.parseDelay);

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
                ParserLog.Write("bitru", $"Starting ParseLatest pages={pages}");

                foreach (var task in taskParse.ToArray())
                {
                    // Get first N pages sorted by page number
                    var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();

                    foreach (var val in pagesToParse)
                    {
                        await Task.Delay(AppInit.conf.Bitru.parseDelay);

                        bool res = await parsePage(task.Key, val.page);
                        if (res)
                        {
                            val.updateTime = DateTime.Today;
                            log += $"{task.Key} - {val.page}\n";
                        }
                    }
                }

                ParserLog.Write("bitru", $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("bitru", $"ParseLatest Error: {ex.Message}");
            }

            _parseLatestWork = false;
            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(string cat, int page)
        {
            string html = await HttpClient.Get($"{AppInit.conf.Bitru.rqHost()}/browse.php?tmp={cat}&page={page}", useproxy: AppInit.conf.Bitru.useproxy);
            if (html == null || !html.Contains("id=\"logo\""))
                return false;

            var torrents = new List<TorrentBaseDetails>();

            foreach (string row in tParse.ReplaceBadNames(html).Split("<div class=\"b-title\"").Skip(1))
            {
                if (row.Contains(">Аниме</a>") || row.Contains(">Мульт"))
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
                DateTime createTime = default;

                if (row.Contains("<span>Сегодня"))
                {
                    createTime = DateTime.UtcNow;
                }
                else if (row.Contains("<span>Вчера"))
                {
                    createTime = DateTime.UtcNow.AddDays(-1);
                }
                else
                {
                    createTime = tParse.ParseCreateTime(Match("<div class=\"ellips\"><span>([0-9]{2} [^ ]+ [0-9]{4}) в [0-9]{2}:[0-9]{2} от <a"), "dd.MM.yyyy");
                }

                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("href=\"(details.php\\?id=[0-9]+)\"");
                string title = Match("<div class=\"it-title\">([^<]+)</div>");
                string _sid = Match("<span class=\"b-seeders\">([0-9]+)</span>");
                string _pir = Match("<span class=\"b-leechers\">([0-9]+)</span>");
                string sizeName = Match("title=\"Размер\">([^<]+)</td>");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                url = $"{AppInit.conf.Bitru.host}/{url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat == "movie")
                {
                    #region Фильмы
                    // Звонок из прошлого / Звонок / Kol / The Call (2020)
                    var g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Код бессмертия / Код молодости / Eternal Code (2019)
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
                            // Брешь / Breach (2020)
                            g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Жертва (2020)
                                g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;

                                name = g[1].Value;
                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                    }
                    #endregion
                }
                else if (cat == "serial")
                {
                    #region Сериалы
                    if (row.Contains("сезон"))
                    {
                        // Золотое Божество 3 сезон (1-12 из 12) / Gōruden Kamui / Golden Kamuy (2020)
                        var g = Regex.Match(title, "^([^/\\(]+) [0-9\\-]+ сезон [^/]+ / [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Ход королевы / Ферзевый гамбит 1 сезон (1-7 из 7) / The Queen's Gambit (2020)
                            g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Доллар 1 сезон (1-15 из 15) / Dollar (2019)
                                // Эш против Зловещих мертвецов 1-3 сезон (1-30 из 30) / Ash vs Evil Dead (2015-2018)
                                g = Regex.Match(title, "^([^/\\(]+) [0-9\\-]+ сезон [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                {
                                    name = g[1].Value;
                                    originalname = g[2].Value;

                                    if (int.TryParse(g[3].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // СашаТаня 6 сезон (1-19 из 22) (2021)
                                    // Метод 1-2 сезон (1-26 из 32) (2015-2020)
                                    g = Regex.Match(title, "^([^/\\(]+) [0-9\\-]+ сезон \\([^\\)]+\\) +\\(([0-9]{4})(\\)|-)").Groups;

                                    name = g[1].Value;
                                    if (int.TryParse(g[2].Value, out int _yer))
                                        relased = _yer;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Проспект обороны (1-16 из 16) (2019)
                        var g = Regex.Match(title, "^([^/\\(]+) \\([^\\)]+\\) +\\(([0-9]{4})(\\)|-)").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[2].Value, out int _yer))
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
                        case "movie":
                            types = new string[] { "movie" };
                            break;
                        case "serial":
                            types = new string[] { "serial" };
                            break;
                    }

                    if (types == null)
                        continue;
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "bitru",
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

                byte[] torrent = await HttpClient.Download(t.url.Replace("/details.php", "/download.php"), referer: t.url, useproxy: AppInit.conf.Bitru.useproxy);
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
