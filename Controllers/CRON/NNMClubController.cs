using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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
using System.Linq;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/nnmclub/[action]")]
    public class NNMClubController : BaseController
    {
        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static NNMClubController()
        {
            if (IO.File.Exists("Data/temp/nnmclub_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/nnmclub_taskParse.json"));
        }

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
                string baseUrl = $"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php";
                ParserLog.Write("nnmclub", $"Starting parse page={page}, base: {baseUrl}");
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
                    ParserLog.Write("nnmclub", $"Category {cat}: {pageUrl}");
                    await parsePage(cat, page);
                    log += $"{cat} - {page}\n";
                }
                ParserLog.Write("nnmclub", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("nnmclub", $"Error: {ex.Message}");
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

                        await Task.Delay(AppInit.conf.NNMClub.parseDelay);

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

            var log = new StringBuilder();

            try
            {
                var sw = Stopwatch.StartNew();
                ParserLog.Write("nnmclub", $"Starting ParseLatest pages={pages}");

                foreach (var task in taskParse.ToArray())
                {
                    // Get first N pages sorted by page number
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

                ParserLog.Write("nnmclub", $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("nnmclub", $"ParseLatest Error: {ex.Message}");
            }
            finally
            {
                _parseLatestSemaphore.Release();
            }

            var logText = log.ToString();
            return string.IsNullOrWhiteSpace(logText) ? "ok" : logText;
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(string cat, int page)
        {
            string html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php?c={cat}&start={page * 20}", encoding: Encoding.GetEncoding(1251), useproxy: AppInit.conf.NNMClub.useproxy);
            if (html == null || !html.Contains("NNM-Club</title>"))
                return false;

            string container = new Regex("<td valign=\"top\" width=\"[0-9]+%\">(.*)<div class=\"paginport nav\">").Match(Regex.Replace(html, "(\n|\r|\t)", "")).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(container))
                return false;

            var torrents = new List<TorrentBaseDetails>();

            foreach (string row in tParse.ReplaceBadNames(container).Split("<table width=\"100%\" class=\"pline\">"))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                // Магнет ссылка
                string magnet = new Regex("\"(magnet:[^\"]+)\"").Match(row).Groups[1].Value;
                if (string.IsNullOrWhiteSpace(magnet))
                    continue;

                #region createTime
                DateTime createTime = tParse.ParseCreateTime(Match("\\| ([0-9]+ [^ ]+ [0-9]{4} [^<]+)</span> \\| <span class=\"tit\""), "dd.MM.yyyy HH:mm:ss");
                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("<a class=\"pgenmed\" href=\"(viewtopic.php[^\"]+)\"");
                string title = Match(">([^<]+)</a></h2></td>");
                string _sid = Match("title=\"Раздаюших\">&nbsp;([0-9]+)</span>", 1);
                string _pir = Match("title=\"Качают\">&nbsp;([0-9]+)</span>", 1);
                string sizeName = Match("<span class=\"pcomm bold\">([^<]+)</span>");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                url = $"{AppInit.conf.NNMClub.host}/forum/{url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat == "10" || cat == "6" || cat == "3" || cat == "22" || cat == "23" || cat == "11")
                {
                    #region Новинки кино / Зарубежное кино / Зарубежные сериалы / Док. TV-бренды / Док. и телепередачи
                    // Крестная мама (Наркомама) / La Daronne / Mama Weed (2020)
                    var g = Regex.Match(title, "^([^/\\(\\|]+) \\([^\\)]+\\) / [^/\\(\\|]+ / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Связанный груз / Белые рабыни-девственницы / Bound Cargo / White Slave Virgins (2003) DVDRip
                        g = Regex.Match(title, "^([^/\\(\\|]+) / [^/\\(\\|]+ / [^/\\(\\|]+ / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Академия монстров / Escuela de Miedo / Cranston Academy: Monster Zone (2020)
                            g = Regex.Match(title, "^([^/\\(\\|]+) / [^/\\(\\|]+ / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Воображаемая реальность (Долина богов) / Valley of the Gods (2019)
                                g = Regex.Match(title, "^([^/\\(\\|]+) \\([^\\)]+\\) / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                {
                                    name = g[1].Value;
                                    originalname = g[2].Value;

                                    if (int.TryParse(g[3].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // Страна грёз / Dreamland (2019)
                                    g = Regex.Match(title, "^([^/\\(\\|]+) / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                    {
                                        name = g[1].Value;
                                        originalname = g[2].Value;

                                        if (int.TryParse(g[3].Value, out int _yer))
                                            relased = _yer;
                                    }
                                    else
                                    {
                                        // Тайны анатомии (Мозг) (2020)
                                        g = Regex.Match(title, "^([^/\\(\\|]+) \\([^\\)]+\\) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                        {
                                            name = g[1].Value;
                                            if (int.TryParse(g[2].Value, out int _yer))
                                                relased = _yer;
                                        }
                                        else
                                        {
                                            // Презумпция виновности (2020)
                                            g = Regex.Match(title, "^([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;

                                            name = g[1].Value;
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
                else if (cat == "13")
                {
                    #region Наше кино
                    var g = Regex.Match(title, "^([^/\\(\\|]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;

                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                    #endregion
                }
                else if (cat == "4")
                {
                    #region Наши сериалы
                    // Теория вероятности / Игрок (2020)
                    var g = Regex.Match(title, "^([^/\\(\\|]+) / [^/\\(\\|]+ \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                    {
                        name = g[1].Value;
                        if (int.TryParse(g[2].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Тайны следствия (2020)
                        g = Regex.Match(title, "^([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                        name = g[1].Value;

                        if (int.TryParse(g[2].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                else if (cat == "1")
                {
                    #region Аниме и Манга
                    // Black Clover (2017) | Чёрный клевер (часть 2) [2017(-2021)?,
                    var g = Regex.Match(title, "^([^/\\[\\(]+) \\([0-9]{4}\\) \\| ([^/\\[\\(]+) \\([^\\)]+\\) \\[([0-9]{4})(-[0-9]{4})?,").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[2].Value;
                        originalname = g[1].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Black Clover (2017) | Чёрный клевер [2017(-2021)?,
                        g = Regex.Match(title, "^([^/\\[\\(]+) \\([0-9]{4}\\) \\| ([^/\\[\\(]+) \\[([0-9]{4})(-[0-9]{4})?,").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[2].Value;
                            originalname = g[1].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Tunshi Xingkong | Swallowed Star | Пожиратель звёзд | Поглощая звезду [2020(-2021)?,
                            // Tunshi Xingkong | Swallowed Star | Пожиратель звёзд | Поглощая звезду [ТВ-1] [2020(-2021)?,
                            g = Regex.Match(title, "^([^/\\[\\(]+) \\| [^/\\[\\(]+ \\| [^/\\[\\(]+ \\| ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                            {
                                name = g[2].Value;
                                originalname = g[1].Value;

                                if (int.TryParse(g[5].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Uzaki-chan wa Asobitai! | Uzaki-chan Wants to Hang Out! | Узаки хочет тусоваться! (Удзаки хочет погулять!) [2020(-2021)?,
                                // Uzaki-chan wa Asobitai! | Uzaki-chan Wants to Hang Out! | Узаки хочет тусоваться! (Удзаки хочет погулять!) [ТВ-1] [2020(-2021)?,
                                g = Regex.Match(title, "^([^/\\[\\(]+) \\| [^/\\[\\(]+ \\| ([^/\\[\\(]+) \\([^\\)]+\\) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                                {
                                    name = g[2].Value;
                                    originalname = g[1].Value;

                                    if (int.TryParse(g[5].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // Kanojo, Okarishimasu | Rent-A-Girlfriend | Девушка на час [ТВ-1] [2020(-2021)?,
                                    // Kusoge-tte Iuna! | Don`t Call Us a Junk Game! | Это вам не трешовая игра! [2020(-2021)?,
                                    g = Regex.Match(title, "^([^/\\[\\(]+) \\| [^/\\[\\(]+ \\| ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,").Groups;
                                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                                    {
                                        name = g[2].Value;
                                        originalname = g[1].Value;

                                        if (int.TryParse(g[5].Value, out int _yer))
                                            relased = _yer;
                                    }
                                    else
                                    {
                                        // Re:Zero kara Hajimeru Isekai Seikatsu 2nd Season | Re: Жизнь в альтернативном мире с нуля [ТВ-2] [2020(-2021)?,
                                        // Hortensia Saga | Сага о гортензии [2021(-2021)?,
                                        g = Regex.Match(title, "^([^/\\[\\(]+) \\| ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,").Groups;
                                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                                        {
                                            name = g[2].Value;
                                            originalname = g[1].Value;

                                            if (int.TryParse(g[5].Value, out int _yer))
                                                relased = _yer;
                                        }
                                        else
                                        {
                                            // Shingeki no Kyojin: The Final Season / Attack on Titan Final Season / Атака титанов. Последний сезон [TV-4] [2020(-2021)?,
                                            g = Regex.Match(title, "^([^/\\[\\(]+) / [^/\\[\\(]+ / ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,").Groups;
                                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                                            {
                                                name = g[2].Value;
                                                originalname = g[1].Value;

                                                if (int.TryParse(g[5].Value, out int _yer))
                                                    relased = _yer;
                                            }
                                            else
                                            {
                                                // Shingeki no Kyojin: The Final Season / Атака титанов. Последний сезон [TV-4] [2020(-2021)?,
                                                g = Regex.Match(title, "^([^/\\[\\(]+) / ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,").Groups;
                                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                                                {
                                                    name = g[2].Value;
                                                    originalname = g[1].Value;

                                                    if (int.TryParse(g[5].Value, out int _yer))
                                                        relased = _yer;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    #endregion
                }
                else if (cat == "7")
                {
                    #region Детям и родителям
                    if (!title.ToLower().Contains("pdf") && (row.Contains("должительность") || row.ToLower().Contains("мульт")))
                    {
                        // Академия монстров / Escuela de Miedo / Cranston Academy: Monster Zone (2020)
                        var g = Regex.Match(title, "^([^/\\(\\|]+) / [^/\\(\\|]+ / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Трансформеры: Война за Кибертрон / Transformers: War For Cybertron (2020)
                            g = Regex.Match(title, "^([^/\\(\\|]+) / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Спина к спине (2020-2021)
                                g = Regex.Match(title, "^([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)").Groups;
                                name = g[1].Value;

                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
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
                        case "10":
                        case "13":
                        case "6":
                        case "11":
                            types = new string[] { "movie" };
                            break;
                        case "4":
                        case "3":
                            types = new string[] { "serial" };
                            break;
                        case "22":
                        case "23":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                        case "7":
                            types = new string[] { "multfilm", "multserial" };
                            break;
                        case "1":
                            types = new string[] { "anime" };
                            break;
                    }

                    if (types == null)
                        continue;
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentBaseDetails()
                    {
                        trackerName = "nnmclub",
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
