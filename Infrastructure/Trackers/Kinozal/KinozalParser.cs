using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Kinozal
{
    public static class KinozalParser
    {
        const string TrackerName = "kinozal";

        /// <summary>
        /// Parse browse-list date column (header «Залит»).
        /// Kinozal shows Обновлен when torrent was re-uploaded; otherwise shows Залит (upload only).
        /// Formats: сегодня/вчера в HH:mm or dd.MM.yyyy в HH:mm.
        /// </summary>
        public static DateTime ParseListingUpdateTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return default;

            raw = Regex.Replace(raw.Trim(), "[\n\r\t ]+", " ");

            var relative = Regex.Match(raw, "^(сегодня|вчера) в ([0-9]{2}):([0-9]{2})$", RegexOptions.IgnoreCase);
            if (relative.Success)
            {
                var baseDate = string.Equals(relative.Groups[1].Value, "сегодня", StringComparison.OrdinalIgnoreCase)
                    ? DateTime.UtcNow.Date
                    : DateTime.UtcNow.Date.AddDays(-1);

                int hour = int.Parse(relative.Groups[2].Value);
                int minute = int.Parse(relative.Groups[3].Value);
                return baseDate.AddHours(hour).AddMinutes(minute);
            }

            var absolute = Regex.Match(raw, "^([0-9]{2})\\.([0-9]{2})\\.([0-9]{4}) в ([0-9]{2}):([0-9]{2})$");
            if (absolute.Success)
            {
                return new DateTime(
                    int.Parse(absolute.Groups[3].Value),
                    int.Parse(absolute.Groups[2].Value),
                    int.Parse(absolute.Groups[1].Value),
                    int.Parse(absolute.Groups[4].Value),
                    int.Parse(absolute.Groups[5].Value),
                    0,
                    DateTimeKind.Utc);
            }

            return tParse.ParseCreateTime(raw, "dd.MM.yyyy");
        }

        public static List<TorrentDetails> ParseTorrentsFromPage(string html, string cat)
        {
            var torrents = new List<TorrentDetails>();

            if (!KinozalCategories.Map.TryGetValue(cat, out var meta))
                return torrents;

            foreach (string row in Regex.Split(tParse.ReplaceBadNames(html), "<tr class=(?:'first bg'|bg)>").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Дата создания
                string listingTime = Match("<td class='sl_p'>[0-9]+</td>\\s*<td class='s'>([^<]+)</td>");
                DateTime createTime = ParseListingUpdateTime(listingTime);

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

                url = $"{AppInit.conf.Kinozal.host}/{url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                switch (meta.TitleKind)
                {
                    case KinozalTitleKind.Movie:
                        ParseMovieTitle(title, out name, out originalname, out relased);
                        break;
                    case KinozalTitleKind.SerialRu:
                        ParseSerialRuTitle(title, row, out name, out relased);
                        break;
                    case KinozalTitleKind.SerialEn:
                        ParseSerialEnTitle(title, row, out name, out originalname, out relased);
                        break;
                    case KinozalTitleKind.TvShow:
                        ParseTvShowTitle(title, out name, out originalname, out relased);
                        break;
                }
                #endregion

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int.TryParse(_sid, out int sid);
                int.TryParse(_pir, out int pir);

                torrents.Add(new TorrentDetails()
                {
                    trackerName = TrackerName,
                    types = meta.Types,
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

            return torrents;
        }

        static void ParseMovieTitle(string title, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

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
                // Name may contain parentheses and season-like slashes (RU-only titles):
                // Голая правда / 2020 / ЛМ / WEB-DLRip
                // Some listings: Title (note) / 2020 / РУ / WEB-DL (1080p)
                var yearField = Regex.Match(title, " / ((?:19|20)[0-9]{2}) / ");
                if (yearField.Success)
                {
                    name = title.Substring(0, yearField.Index).Trim();
                    if (int.TryParse(yearField.Groups[1].Value, out int _yer))
                        relased = _yer;
                }
            }
        }

        static void ParseSerialRuTitle(string title, string row, out string name, out int relased)
        {
            name = null;
            relased = 0;

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
        }

        static void ParseSerialEnTitle(string title, string row, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

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
        }

        static void ParseTvShowTitle(string title, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

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
        }

        /// <summary>
        /// Кинозал при добавлении серий/озвучек перехеширует .torrent (новый info hash),
        /// но title в списке часто не меняется — раньше hash не перезапрашивался.
        /// createTime = date from browse «Залит» column: Обновлен if present on details,
        /// otherwise original Залит (never re-uploaded).
        /// </summary>
        internal static bool ShouldSkipHashFetch(TorrentDetails cached, TorrentDetails parsed)
        {
            if (string.IsNullOrWhiteSpace(cached.magnet))
                return false;

            if (cached.title != parsed.title)
                return false;

            if (cached.sizeName != parsed.sizeName)
                return false;

            // Обновлен changed (including time on the same day) → rehash likely
            if (parsed.createTime > cached.createTime)
                return false;

            return true;
        }
    }
}
