using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Engine.Parsing;
using JacRed.Models.Details;

namespace JacRed.Engine.Trackers.Kinozal
{
    public static class KinozalParser
    {
        const string TrackerName = "kinozal";

        public static List<TorrentDetails> ParseTorrentsFromPage(string html, string cat)
        {
            var torrents = new List<TorrentDetails>();

            foreach (string row in Regex.Split(tParse.ReplaceBadNames(html), "<tr class=('first bg'|bg)>").Skip(1))
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
                DateTime createTime = default;

                if (row.Contains("<td class='s'>сегодня"))
                {
                    createTime = DateTime.UtcNow;
                }
                else if (row.Contains("<td class='s'>вчера"))
                {
                    createTime = DateTime.UtcNow.AddDays(-1);
                }
                else
                {
                    createTime = tParse.ParseCreateTime(Match("<td class='s'>([0-9]{2}.[0-9]{2}.[0-9]{4}) в [0-9]{2}:[0-9]{2}</td>"), "dd.MM.yyyy");
                }

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

                url = "http://kinozal.tv/" + url;
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat is "8" or "6" or "15" or "17" or "35" or "39" or "13" or "14" or "24" or "11" or "9" or "47" or "18" or "37" or "12" or "10" or "7" or "16")
                {
                    #region Фильмы
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
                        // Голая правда / 2020 / ЛМ / WEB-DLRip
                        g = Regex.Match(title, "^([^/\\(]+) / ([0-9]{4})").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[2].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                else if (cat == "45" || cat == "22")
                {
                    #region Сериал - Русский
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
                    #endregion
                }
                else if (cat == "46" || cat == "21" || cat == "20")
                {
                    #region Сериал - Буржуйский
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
                    #endregion
                }
                else if (cat == "49" || cat == "50")
                {
                    #region ТВ-шоу
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
                        case "8":
                        case "6":
                        case "15":
                        case "17":
                        case "35":
                        case "39":
                        case "13":
                        case "14":
                        case "24":
                        case "11":
                        case "9":
                        case "47":
                        case "18":
                        case "37":
                        case "12":
                        case "10":
                        case "7":
                        case "16":
                            types = new string[] { "movie" };
                            break;
                        case "45":
                        case "46":
                            types = new string[] { "serial" };
                            break;
                        case "49":
                        case "50":
                            types = new string[] { "tvshow" };
                            break;
                        case "21":
                        case "22":
                            types = new string[] { "multfilm", "multserial" };
                            break;
                        case "20":
                            types = new string[] { "anime" };
                            break;
                    }

                    if (types == null)
                        continue;
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = TrackerName,
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

            return torrents;
        }
    }
}
