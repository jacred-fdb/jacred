using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.NNMClub
{
    public static class NNMClubParser
    {
        const string TrackerName = "nnmclub";

        public static List<TorrentBaseDetails> ParseTorrentsFromPage(string html, string cat)
        {
            string container = new Regex("<td valign=\"top\" width=\"[0-9]+%\">(.*)<div class=\"paginport nav\">").Match(Regex.Replace(html, "(\n|\r|\t)", "")).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(container))
                return new List<TorrentBaseDetails>();

            var torrents = new List<TorrentBaseDetails>();

            foreach (string row in tParse.ReplaceBadNames(container).Split("<table width=\"100%\" class=\"pline\">"))
            {
                string magnet = new Regex("\"(magnet:[^\"]+)\"").Match(row).Groups[1].Value;
                if (string.IsNullOrWhiteSpace(magnet))
                    continue;

                if (!TryParseRowFields(row, out string url, out string title, out string sid, out string pir, out string sizeName, out DateTime createTime))
                    continue;

                ParseTitleNames(cat, title, row, out string name, out string originalname, out int relased);

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    string[] types = GetTypesForCategory(cat);
                    if (types == null)
                        continue;

                    int.TryParse(sid, out int sidInt);
                    int.TryParse(pir, out int pirInt);

                    torrents.Add(new TorrentBaseDetails()
                    {
                        trackerName = TrackerName,
                        types = types,
                        url = url,
                        title = title,
                        sid = sidInt,
                        pir = pirInt,
                        sizeName = sizeName,
                        magnet = magnet,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            return torrents;
        }

        private static string MatchRow(string row, string pattern, int index = 1)
        {
            string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
            res = Regex.Replace(res, "[\n\r\t ]+", " ");
            return res.Trim();
        }

        private static bool TryParseCreateTime(string row, out DateTime createTime)
        {
            createTime = tParse.ParseCreateTime(MatchRow(row, "\\| ([0-9]+ [^ ]+ [0-9]{4} [^<]+)</span> \\| <span class=\"tit\""), "dd.MM.yyyy HH:mm:ss");
            return createTime != default;
        }

        private static bool TryParseRowFields(string row, out string url, out string title, out string sid, out string pir, out string sizeName, out DateTime createTime)
        {
            if (!TryParseCreateTime(row, out createTime))
            {
                url = null;
                title = null;
                sid = null;
                pir = null;
                sizeName = null;
                return false;
            }

            url = MatchRow(row, "<a class=\"pgenmed\" href=\"(viewtopic.php[^\"]+)\"");
            title = MatchRow(row, ">([^<]+)</a></h2></td>");
            sid = MatchRow(row, "title=\"Раздаюших\">&nbsp;([0-9]+)</span>", 1);
            pir = MatchRow(row, "title=\"Качают\">&nbsp;([0-9]+)</span>", 1);
            sizeName = MatchRow(row, "<span class=\"pcomm bold\">([^<]+)</span>");

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(pir) || string.IsNullOrWhiteSpace(sizeName))
            {
                url = null;
                title = null;
                sid = null;
                pir = null;
                sizeName = null;
                return false;
            }

            url = $"{AppInit.conf.NNMClub.host}/forum/{url}";
            return true;
        }

        private static void ParseTitleNames(string cat, string title, string row, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

            if (cat == "10" || cat == "6" || cat == "3" || cat == "22" || cat == "23" || cat == "11")
            {
                (name, originalname, relased) = ParseForeignCinemaTitle(title);
            }
            else if (cat == "13")
            {
                (name, originalname, relased) = ParseDomesticMovieTitle(title);
            }
            else if (cat == "4")
            {
                (name, originalname, relased) = ParseDomesticSerialTitle(title);
            }
            else if (cat == "1")
            {
                (name, originalname, relased) = ParseAnimeTitle(title);
            }
            else if (cat == "7")
            {
                (name, originalname, relased) = ParseKidsTitle(title, row);
            }
        }

        private static (string name, string originalname, int relased) ParseForeignCinemaTitle(string title)
        {
            string name = null, originalname = null;
            int relased = 0;

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

            return (name, originalname, relased);
        }

        private static (string name, string originalname, int relased) ParseDomesticMovieTitle(string title)
        {
            string name = null;
            int relased = 0;

            var g = Regex.Match(title, "^([^/\\(\\|]+) \\(([0-9]{4})\\)").Groups;
            name = g[1].Value;

            if (int.TryParse(g[2].Value, out int _yer))
                relased = _yer;

            return (name, null, relased);
        }

        private static (string name, string originalname, int relased) ParseDomesticSerialTitle(string title)
        {
            string name = null;
            int relased = 0;

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

            return (name, null, relased);
        }

        private static (string name, string originalname, int relased) ParseAnimeTitle(string title)
        {
            string name = null, originalname = null;
            int relased = 0;

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

            return (name, originalname, relased);
        }

        private static (string name, string originalname, int relased) ParseKidsTitle(string title, string row)
        {
            string name = null, originalname = null;
            int relased = 0;

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

            return (name, originalname, relased);
        }

        private static string[] GetTypesForCategory(string cat)
        {
            switch (cat)
            {
                case "10":
                case "13":
                case "6":
                case "11":
                    return new string[] { "movie" };
                case "4":
                case "3":
                    return new string[] { "serial" };
                case "22":
                case "23":
                    return new string[] { "docuserial", "documovie" };
                case "7":
                    return new string[] { "multfilm", "multserial" };
                case "1":
                    return new string[] { "anime" };
                default:
                    return null;
            }
        }
    }
}
