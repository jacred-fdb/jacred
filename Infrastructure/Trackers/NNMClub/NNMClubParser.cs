using System;
using System.Collections.Generic;
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
            if (!NNMClubCategories.Map.TryGetValue(cat, out var meta))
                return new List<TorrentBaseDetails>();

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

                if (title.ToLower().Contains("трейлер"))
                    continue;

                if (meta.SkipPdfInTitle && title.ToLower().Contains("pdf"))
                    continue;

                if (meta.RequireMultInRow && !RowLooksLikeCartoon(row))
                    continue;

                ParseTitleNames(meta.TitleKind, title, out string name, out string originalname, out int relased);

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int.TryParse(sid, out int sidInt);
                int.TryParse(pir, out int pirInt);

                torrents.Add(new TorrentBaseDetails()
                {
                    trackerName = TrackerName,
                    types = meta.Types,
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

            return torrents;
        }

        static bool RowLooksLikeCartoon(string row)
        {
            string lower = row.ToLower();
            return lower.Contains("мульт")
                || lower.Contains("длительность")
                || lower.Contains("продолжительность");
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
            sid = MatchRow(row, "title=\"Раздаю[щш]их\">&nbsp;([0-9]+)</span>", 1);
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

        private static void ParseTitleNames(NNMClubTitleKind titleKind, string title, out string name, out string originalname, out int relased)
        {
            (name, originalname, relased) = titleKind switch
            {
                NNMClubTitleKind.ForeignCinema => ParseForeignCinemaTitle(title),
                NNMClubTitleKind.ForeignSerial => ParseForeignCinemaTitle(title),
                NNMClubTitleKind.RuMovie => ParseDomesticMovieTitle(title),
                NNMClubTitleKind.RuSerial => ParseDomesticSerialTitle(title),
                NNMClubTitleKind.Anime => ParseAnimeTitle(title),
                NNMClubTitleKind.KidsMult => ParseKidsTitle(title),
                NNMClubTitleKind.ShowLike => ParseForeignCinemaTitle(title),
                NNMClubTitleKind.Sport => ParseForeignCinemaTitle(title),
                _ => (null, null, 0)
            };
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

        private static (string name, string originalname, int relased) ParseKidsTitle(string title)
        {
            string name = null, originalname = null;
            int relased = 0;

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

            return (name, originalname, relased);
        }
    }
}
