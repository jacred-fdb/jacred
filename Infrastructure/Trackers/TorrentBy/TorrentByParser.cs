using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.TorrentBy
{
    public static class TorrentByParser
    {
        const string TrackerName = "torrentby";

        public static async Task<bool> ParsePageAsync(string cat, int page)
        {
            string html = await HttpClient.Get($"{AppInit.conf.TorrentBy.rqHost()}/{cat}/?page={page}", useproxy: AppInit.conf.TorrentBy.useproxy);
            if (html == null)
                return false;

            var torrents = ParseTorrentsFromHtml(html, cat);
            FileDB.AddOrUpdate(torrents);
            return torrents.Count > 0;
        }

        public static List<TorrentBaseDetails> ParseTorrentsFromHtml(string html, string cat)
        {
            var torrents = new List<TorrentBaseDetails>();

            if (!TorrentByCategories.Map.TryGetValue(cat, out var meta))
                return torrents;

            foreach (string row in tParse.ReplaceBadNames(html).Split("<tr class=\"ttable_col").Skip(1))
            {
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }

                if (string.IsNullOrWhiteSpace(row) || !row.Contains("magnet:?xt=urn"))
                    continue;

                DateTime createTime = default;

                if (row.Contains(">Сегодня</td>"))
                    createTime = DateTime.UtcNow;
                else if (row.Contains(">Вчера</td>"))
                    createTime = DateTime.UtcNow.AddDays(-1);
                else
                {
                    string _createTime = Match(">([0-9]{4}-[0-9]{2}-[0-9]{2})</td>").Replace("-", " ");
                    if (!DateTime.TryParseExact(_createTime, "yyyy MM dd", new CultureInfo("ru-RU"), DateTimeStyles.None, out createTime))
                        continue;
                }

                if (createTime == default)
                    continue;

                string url = Match("<a name=\"search_select\" [^>]+ href=\"/([0-9]+/[^\"]+)\"");
                string title = Match("<a name=\"search_select\" [^>]+>([^<]+)</a>");
                string _sid = Match("<font color=\"green\">&uarr; ([0-9]+)</font>");
                string _pir = Match("<font color=\"red\">&darr; ([0-9]+)</font>");
                string sizeName = Match("</td><td style=\"white-space:nowrap;\">([^<]+)</td>");
                string magnet = Match("href=\"(magnet:\\?xt=[^\"]+)\"");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName) || string.IsNullOrWhiteSpace(magnet))
                    continue;

                url = $"{AppInit.conf.TorrentBy.host}/{url}";

                int relased = 0;
                string name = null, originalname = null;

                switch (meta.TitleKind)
                {
                    case TorrentByTitleKind.FilmsForeign:
                        ParseFilmsForeign(title, out name, out originalname, out relased);
                        break;
                    case TorrentByTitleKind.FilmsRu:
                        ParseFilmsRu(title, out name, out relased);
                        break;
                    case TorrentByTitleKind.SerialForeign:
                        ParseSerialForeign(title, out name, out originalname, out relased);
                        break;
                    case TorrentByTitleKind.SerialRu:
                        ParseSerialRu(title, out name, out relased);
                        break;
                    case TorrentByTitleKind.ShowLike:
                        ParseShowLike(title, out name, out originalname, out relased);
                        break;
                    case TorrentByTitleKind.Sport:
                        ParseSport(title, out name, out relased);
                        break;
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int.TryParse(_sid, out int sid);
                int.TryParse(_pir, out int pir);

                torrents.Add(new TorrentBaseDetails
                {
                    trackerName = TrackerName,
                    types = meta.Types,
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

            return torrents;
        }

        /// <summary>
        /// Зарубежные фильмы: Name / Alt / Orig (year) … or Name / Orig (year) …
        /// </summary>
        static void ParseFilmsForeign(string title, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

            // Только течёт река / He bian de cuo wu / Only the River Flows (2023) BDRip…
            var g = Regex.Match(title, @"^([^/\(]+) / [^/]+ / ([^/\(]+) \(((?:19|20)[0-9]{2})\)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value.Trim();
                originalname = g[2].Value.Trim();
                if (int.TryParse(g[3].Value, out int yer))
                    relased = yer;
                return;
            }

            // Запретный плод / Forbidden Fruits (2025) WEB-DLRip…
            g = Regex.Match(title, @"^([^/\(]+) / ([^/\(]+) \(((?:19|20)[0-9]{2})\)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value.Trim();
                originalname = g[2].Value.Trim();
                if (int.TryParse(g[3].Value, out int yer))
                    relased = yer;
            }
        }

        /// <summary>
        /// Наши фильмы: Name (year) … or Name [year, genres…] …
        /// </summary>
        static void ParseFilmsRu(string title, out string name, out int relased)
        {
            name = null;
            relased = 0;

            // Не одна дома 3. Выпускной (2026) WEB-DL…
            var g = Regex.Match(title, @"^([^/\(\[]+) \(((?:19|20)[0-9]{2})\)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                name = g[1].Value.Trim();
                if (int.TryParse(g[2].Value, out int yer))
                    relased = yer;
                return;
            }

            // Фронт в тылу врага / Серии: 1-2 из 2 [1981, драма, …]
            g = Regex.Match(title, @"^([^/\(\[]+)(?: / [^/\[]+)? \[((?:19|20)[0-9]{2})").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                name = g[1].Value.Trim();
                if (int.TryParse(g[2].Value, out int yer))
                    relased = yer;
            }
        }

        /// <summary>
        /// Зарубежные сериалы: Name / Orig (year) … / Name / Alt / Orig [S01] (year) …
        /// </summary>
        static void ParseSerialForeign(string title, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

            // Скитальцы / Искатели пути / The Wayfinders [S01] (2025) …
            var g = Regex.Match(title, @"^([^/\(\[]+) / [^/]+ / ([^/\[\(]+)(?: \[[^\]]+\])? \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value.Trim();
                originalname = g[2].Value.Trim();
                if (int.TryParse(g[3].Value, out int yer))
                    relased = yer;
                return;
            }

            // Дом Дракона / House of the Dragon (2026) …  OR  Name / Orig [S01] (2025)
            g = Regex.Match(title, @"^([^/\(\[]+) / ([^/\[\(]+)(?: \[[^\]]+\])? \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value.Trim();
                originalname = g[2].Value.Trim();
                if (int.TryParse(g[3].Value, out int yer))
                    relased = yer;
                return;
            }

            // Дом Дракона / House of the Dragon [03x01-04 из 08] (2026)
            g = Regex.Match(title, @"^([^/\(\[]+) / ([^/\[\(]+) \[[^\]]+\] \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value.Trim();
                originalname = g[2].Value.Trim();
                if (int.TryParse(g[3].Value, out int yer))
                    relased = yer;
                return;
            }

            // 5:32 (2020-2022) …  or Name [S01] (2026)
            g = Regex.Match(title, @"^([^/\(\[]+)(?: \[[^\]]+\])? \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                name = g[1].Value.Trim();
                if (int.TryParse(g[2].Value, out int yer))
                    relased = yer;
            }
        }

        /// <summary>
        /// Наши сериалы: Name / Сезон: … / Серии: … [year…]  or Name [01x01…] (year)
        /// </summary>
        static void ParseSerialRu(string title, out string name, out int relased)
        {
            name = null;
            relased = 0;

            // Доктор, я боюсь / Сезон: 1 / Серии: 1-36 из 40 [2025-2026, мелодрама, …]
            var g = Regex.Match(title, @"^([^/\(\[]+) / Сезон:[^\[]+\[((?:19|20)[0-9]{2})").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                name = g[1].Value.Trim();
                if (int.TryParse(g[2].Value, out int yer))
                    relased = yer;
                return;
            }

            // Холод [01х01 из 10] (2026) WEB-DL…
            g = Regex.Match(title, @"^([^/\(\[]+) \[[^\]]+\] \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                name = g[1].Value.Trim();
                if (int.TryParse(g[2].Value, out int yer))
                    relased = yer;
                return;
            }

            g = Regex.Match(title, @"^([^/\(\[]+) \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                name = g[1].Value.Trim();
                if (int.TryParse(g[2].Value, out int yer))
                    relased = yer;
            }
        }

        /// <summary>
        /// tv / humor / cartoons / anime — slash+orig+(year), [Sxx] (year), or Name (year).
        /// </summary>
        static void ParseShowLike(string title, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

            if (title.Contains(" / "))
            {
                // Name / Alt / Orig [S01] (year)  or  Name / Orig (year)
                var g = Regex.Match(title, @"^([^/\(\[]+) / [^/]+ / ([^/\[\(]+)(?: \[[^\]]+\])? \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value.Trim();
                    originalname = g[2].Value.Trim();
                    if (int.TryParse(g[3].Value, out int yer))
                        relased = yer;
                    return;
                }

                g = Regex.Match(title, @"^([^/\(\[]+) / ([^/\[\(]+)(?: \[[^\]]+\])? \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value.Trim();
                    originalname = g[2].Value.Trim();
                    if (int.TryParse(g[3].Value, out int yer))
                        relased = yer;
                    return;
                }
            }

            // Stand Up [13x01-28] (2025) …  /  Name [S10] (2023)
            var g2 = Regex.Match(title, @"^([^/\(\[]+) \[[^\]]+\] \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g2[1].Value) && !string.IsNullOrWhiteSpace(g2[2].Value))
            {
                name = g2[1].Value.Trim();
                if (int.TryParse(g2[2].Value, out int yer))
                    relased = yer;
                return;
            }

            g2 = Regex.Match(title, @"^([^/\(\[]+) \(((?:19|20)[0-9]{2})(?:\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g2[1].Value) && !string.IsNullOrWhiteSpace(g2[2].Value))
            {
                name = g2[1].Value.Trim();
                if (int.TryParse(g2[2].Value, out int yer))
                    relased = yer;
            }
        }

        /// <summary>
        /// Sport titles often contain 1/2 path-like slashes; take name before year in parentheses.
        /// Футбол. … 1/2 финала. … [15.07] (2025) WEBRip…
        /// </summary>
        static void ParseSport(string title, out string name, out int relased)
        {
            name = null;
            relased = 0;

            var yearMatch = Regex.Match(title, @"\(((?:19|20)[0-9]{2})\)");
            if (!yearMatch.Success)
                return;

            if (int.TryParse(yearMatch.Groups[1].Value, out int yer))
                relased = yer;

            name = title.Substring(0, yearMatch.Index).Trim();
            // Drop trailing [dd.mm] date markers common on torrent.by sport listings
            name = Regex.Replace(name, @"\s*\[[0-9]{1,2}\.[0-9]{1,2}\]\s*$", "").Trim();
        }
    }
}
