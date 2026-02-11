using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
using JacRed.Models.Details;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/torrentby/[action]")]
    public class TorrentByController : BaseController
    {
        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static TorrentByController()
        {
            if (IO.File.Exists("Data/temp/torrentby_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/torrentby_taskParse.json"));
        }


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
                string baseUrl = AppInit.conf.TorrentBy.rqHost();
                ParserLog.Write("torrentby", $"Starting parse page={page}, base: {baseUrl}");
                // films     - Зарубежные фильмы    | Фильмы
                // movies    - Наши фильмы          | Фильмы
                // serials   - Сериалы              | Сериалы
                // tv        - Телевизор            | ТВ Шоу
                // humor     - Юмор                 | ТВ Шоу
                // cartoons  - Мультфильмы          | Мультфильмы, Мультсериалы
                // anime     - Аниме                | Аниме
                // sport     - Спорт                | Спорт
                foreach (string cat in new List<string>() { "films", "movies", "serials", "tv", "humor", "cartoons", "anime", "sport" })
                {
                    string pageUrl = $"{baseUrl}/{cat}/?page={page}";
                    ParserLog.Write("torrentby", $"Category {cat}: {pageUrl}");
                    await parsePage(cat, page);
                    log += $"{cat} - {page}\n";
                }
                ParserLog.Write("torrentby", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("torrentby", $"Error: {ex.Message}");
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
            foreach (string cat in new List<string>() { "films", "movies", "serials", "tv", "humor", "cartoons", "anime", "sport" })
            {
                // Получаем html
                string html = await HttpClient.Get($"{AppInit.conf.TorrentBy.rqHost()}/{cat}/", timeoutSeconds: 10, useproxy: AppInit.conf.TorrentBy.useproxy);
                if (html == null)
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, "href=\"\\?page=([0-9]+)\">[0-9]+</a>([\t ]+)?</center></td>").Groups[1].Value, out int maxpages);

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

            IO.File.WriteAllText("Data/temp/torrentby_taskParse.json", JsonConvert.SerializeObject(taskParse));
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

                        await Task.Delay(AppInit.conf.TorrentBy.parseDelay);

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
                ParserLog.Write("torrentby", $"Starting ParseLatest pages={pages}");

                foreach (var task in taskParse.ToArray())
                {
                    // Get first N pages sorted by page number
                    var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();

                    foreach (var val in pagesToParse)
                    {
                        await Task.Delay(AppInit.conf.TorrentBy.parseDelay);

                        bool res = await parsePage(task.Key, val.page);
                        if (res)
                        {
                            val.updateTime = DateTime.Today;
                            log += $"{task.Key} - {val.page}\n";
                        }
                    }
                }

                ParserLog.Write("torrentby", $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("torrentby", $"ParseLatest Error: {ex.Message}");
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
            string html = await HttpClient.Get($"{AppInit.conf.TorrentBy.rqHost()}/{cat}/?page={page}", useproxy: AppInit.conf.TorrentBy.useproxy);
            if (html == null)
                return false;

            var torrents = new List<TorrentBaseDetails>();

            foreach (string row in tParse.ReplaceBadNames(html).Split("<tr class=\"ttable_col").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row) || !row.Contains("magnet:?xt=urn"))
                    continue;

                #region Дата создания
                DateTime createTime = default;

                if (row.Contains(">Сегодня</td>"))
                {
                    createTime = DateTime.UtcNow;
                }
                else if (row.Contains(">Вчера</td>"))
                {
                    createTime = DateTime.UtcNow.AddDays(-1);
                }
                else
                {
                    string _createTime = Match(">([0-9]{4}-[0-9]{2}-[0-9]{2})</td>").Replace("-", " ");
                    if (!DateTime.TryParseExact(_createTime, "yyyy MM dd", new CultureInfo("ru-RU"), DateTimeStyles.None, out createTime))
                        continue;
                }

                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("<a name=\"search_select\" [^>]+ href=\"/([0-9]+/[^\"]+)\"");
                string title = Match("<a name=\"search_select\" [^>]+>([^<]+)</a>");
                string _sid = Match("<font color=\"green\">&uarr; ([0-9]+)</font>");
                string _pir = Match("<font color=\"red\">&darr; ([0-9]+)</font>");
                string sizeName = Match("</td><td style=\"white-space:nowrap;\">([^<]+)</td>");
                string magnet = Match("href=\"(magnet:\\?xt=[^\"]+)\"");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName) || string.IsNullOrWhiteSpace(magnet))
                    continue;

                url = $"{AppInit.conf.TorrentBy.host}/{url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat == "films")
                {
                    #region Зарубежные фильмы
                    // Код бессмертия / Код молодости / Eternal Code (2019)
                    var g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
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

                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                else if (cat == "movies")
                {
                    #region Наши фильмы
                    // Временная связь (2020)
                    // Приключения принца Флоризеля / Клуб самоубийц или Приключения титулованной особы (1979)
                    var g = Regex.Match(title, "^([^/\\(]+) (/ [^/\\(]+)?\\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                    #endregion
                }
                else if (cat == "serials")
                {
                    #region Сериалы
                    // Голяк / Без гроша / Без денег / Brassic [S04] (2022) WEB-DLRip | Ozz
                    var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/]+ / [^/]+ / ([^/\\(\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Перевал / Der Pass / Pagan Peak [S01] (2018)
                        g = Regex.Match(title, "^([^/\\(\\[]+) / [^/]+ / ([^/\\(\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Стража / The Watch [01x01-05 из 08] (2020)
                            g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Стажёры [01-10 из 24] (2019)
                                g = Regex.Match(title, "^([^/\\(\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;

                                name = g[1].Value;
                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                    }
                    #endregion
                }
                else if (cat == "cartoons" || cat == "anime" || cat == "tv" || cat == "humor" || cat == "sport")
                {
                    #region Мультфильмы / Аниме / Телевизор / Юмор / Спорт
                    if (title.Contains(" / "))
                    {
                        if (title.Contains("[") && title.Contains("]"))
                        {
                            // 	Разочарование / еще название / Disenchantment [S03] (2021)
                            var g = Regex.Match(title, "^([^/]+) / [^/]+ / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // 	Разочарование / Disenchantment [S03] (2021)
                                g = Regex.Match(title, "^([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;

                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                        else
                        {
                            // 	Душа / еще название / Soul (2020)
                            var g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Душа / Soul (2020)
                                // Галактики / Galaxies (2017-2019)
                                g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;

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
                            // 	Непокоренные [01-04 из 04] (2020)
                            var g = Regex.Match(title, "^([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                            name = g[1].Value;

                            if (int.TryParse(g[2].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Душа (2020)
                            var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
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
                    string[] types = null;
                    switch (cat)
                    {
                        case "films":
                        case "movies":
                            types = new string[] { "movie" };
                            break;
                        case "serials":
                            types = new string[] { "serial" };
                            break;
                        case "tv":
                        case "humor":
                            types = new string[] { "tvshow" };
                            break;
                        case "cartoons":
                            types = new string[] { "multfilm", "multserial" };
                            break;
                        case "anime":
                            types = new string[] { "anime" };
                            break;
                        case "sport":
                            types = new string[] { "sport" };
                            break;
                    }

                    if (types == null)
                        continue;
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentBaseDetails()
                    {
                        trackerName = "torrentby",
                        types = types,
                        url = url,
                        title = title,
                        sid = sid,
                        pir = pir,
                        sizeName = sizeName,
                        magnet = magnet,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            FileDB.AddOrUpdate(torrents);
            return torrents.Count > 0;
        }
        #endregion
    }
}
