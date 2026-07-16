using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Rutracker
{
    public static class RutrackerParser
    {
        const string TrackerName = "rutracker";

        public static List<TorrentDetails> ParseTorrentsFromPage(string html, string cat)
        {
            var torrents = new List<TorrentDetails>();

            if (!RutrackerCategories.Map.TryGetValue(cat, out var meta))
                return torrents;

            foreach (string row in tParse.ReplaceBadNames(html).Split("class=\"torTopic\"").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;

                if (!TryParseCreateTime(row, out DateTime createTime))
                    continue;

                if (!TryParseRowFields(row, out string url, out string title, out string sid, out string pir, out string sizeName))
                    continue;

                var (name, originalname, relased, skipRow) = ParseTitleNames(meta.TitleKind, title);
                if (skipRow)
                    continue;

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    int.TryParse(sid, out int sidNum);
                    int.TryParse(pir, out int pirNum);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = TrackerName,
                        types = meta.Types,
                        url = url,
                        title = title,
                        sid = sidNum,
                        pir = pirNum,
                        sizeName = sizeName,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            return torrents;
        }

        public static bool ApplyTopicPageDetails(TorrentDetails t, string fullNews)
        {
            if (fullNews == null)
                return false;

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

            return false;
        }

        static string MatchRow(string row, string pattern, int index = 1)
        {
            string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
            res = Regex.Replace(res, "[\n\r\t ]+", " ");
            return res.Trim();
        }

        static bool TryParseCreateTime(string row, out DateTime createTime)
        {
            if (!DateTime.TryParse(MatchRow(row, "<p>([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})</p>"), out createTime) || createTime == default)
            {
                createTime = default;
                return false;
            }

            return true;
        }

        static bool TryParseRowFields(string row, out string url, out string title, out string sid, out string pir, out string sizeName)
        {
            url = MatchRow(row, "<a id=\"tt-([0-9]+)\"");
            title = MatchRow(row, "<a id=\"tt-[0-9]+\"[^>]+>([^\n\r]+)</a>");
            title = Regex.Replace(title, "<[^>]+>", "");
            sid = MatchRow(row, "<span class=\"seedmed\"[^>]+><b>([0-9]+)</b>");
            pir = MatchRow(row, "<span class=\"leechmed\"[^>]+><b>([0-9]+)</b>");
            sizeName = MatchRow(row, "dl-stub\">([^<]+)</a>").Replace("&nbsp;", " ");

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(pir) || string.IsNullOrWhiteSpace(sizeName))
            {
                url = title = sid = pir = sizeName = null;
                return false;
            }

            url = $"{AppInit.conf.Rutracker.host}/forum/viewtopic.php?t={url}";
            return true;
        }

        static (string name, string originalname, int relased, bool skipRow) ParseTitleNames(RutrackerTitleKind titleKind, string title)
        {
            return titleKind switch
            {
                RutrackerTitleKind.Movie => ParseMovieTitle(title),
                RutrackerTitleKind.Serial => ParseSerialTitle(title),
                RutrackerTitleKind.NonStandard => ParseNonStandardTitle(title),
                _ => (null, null, 0, false)
            };
        }

        static (string name, string originalname, int relased, bool skipRow) ParseMovieTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

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

            if (name != null)
                name = name.Replace("в 3Д", "").Trim();

            if (originalname != null)
                originalname = originalname.Replace(" in 3D", "").Replace(" 3D", "").Trim();

            return (name, originalname, relased, false);
        }

        static (string name, string originalname, int relased, bool skipRow) ParseSerialTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

            if (Regex.IsMatch(title, "(Сезон|Серии)", RegexOptions.IgnoreCase))
            {
                if (title.Contains("Сезон:"))
                {
                    // Голяк / Без гроша / Без денег / Brassic / Сезон: 4 / Серии: 1-8 из 8 (Джон Райт, Дэниэл О'Хара, Сауль Метцштайн, Джон Хардвик) [2022, Великобритания, Комедия, криминал, WEB-DLRip] MVO (Ozz) + Original + Sub (Rus, Ukr, Eng)
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

            return (name, originalname, relased, false);
        }

        static (string name, string originalname, int relased, bool skipRow) ParseNonStandardTitle(string title)
        {
            int relased = 0;
            string name = Regex.Match(title, "^([^/\\(\\[]+) ").Groups[1].Value;

            if (int.TryParse(Regex.Match(title, " \\[([0-9]{4})(,|-) ").Groups[1].Value, out int _yer))
                relased = _yer;

            if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                return (name, null, relased, true);

            return (name, null, relased, false);
        }
    }
}
