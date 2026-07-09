using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Engine.Parsing;
using JacRed.Models.Details;

namespace JacRed.Engine.Trackers.Toloka
{
    public static class TolokaParser
    {
        const string TrackerName = "toloka";

        public static List<TolokaDetails> ParseTorrentsFromPage(string html, string cat)
        {
            var torrents = new List<TolokaDetails>();

            foreach (string row in tParse.ReplaceBadNames(html).Split("</tr>").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row) || Regex.IsMatch(row, "Збір коштів", RegexOptions.IgnoreCase))
                    continue;

                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                #region Дата создания
                string _createTime = Match("class=\"postdetails\">([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})").Replace("-", ".");
                if (!DateTime.TryParse(_createTime, out DateTime createTime) || createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("<a href=\"(t[0-9]+)\" class=\"topictitle\"");
                string title = Match("class=\"topictitle\">([^<]+)</a>");

                string _sid = Match("<span class=\"seedmed\" [^>]+><b>([0-9]+)</b></span>");
                string _pir = Match("<span class=\"leechmed\" [^>]+><b>([0-9]+)</b></span>");
                string sizeName = Match("<a href=\"download.php[^\"]+\" [^>]+>([^<]+)</a>").Replace("&nbsp;", " ");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName) || sizeName == "0 B")
                    continue;

                url = $"{AppInit.conf.Toloka.host}/{url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat is "16" or "96" or "19" or "139" or "12" or "131" or "84" or "42")
                {
                    #region Фильмы
                    // Незворотність / Irréversible / Irreversible (2002) AVC Ukr/Fre | Sub Eng
                    var g = Regex.Match(title, "^([^/\\(\\[]+)/[^/\\(\\[]+/([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value.Trim();
                        originalname = g[2].Value.Trim();

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Мій рік у Нью-Йорку / My Salinger Year (2020) Ukr/Eng
                        g = Regex.Match(title, "^([^/\\(\\[]+)/([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value.Trim();
                            originalname = g[2].Value.Trim();

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Хроніка надій та ілюзій. Дзеркало історії. (83 серії) (2001-2003) PDTVRip
                            g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                            {
                                name = g[1].Value;

                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Берестечко. Битва за Україну (2015-2016) DVDRip-AVC
                                g = Regex.Match(title, "^([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                {
                                    name = g[1].Value;

                                    if (int.TryParse(g[2].Value, out int _yer))
                                        relased = _yer;
                                }
                            }
                        }
                    }
                    #endregion
                }
                else if (cat is "32" or "173" or "174" or "44" or "230" or "226" or "227" or "228" or "229" or "127" or "124" or "125" or "132")
                {
                    #region Сериалы
                    // Атака титанів (Attack on Titan) (Сезон 1) / Shingeki no Kyojin (Season 1) (2013) BDRip 720р
                    var g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\([^\\)]+\\) ?/([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value.Trim();
                        originalname = g[2].Value.Trim();

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Дім з прислугою (Сезон 2, серії 1-8) / Servant (Season 2, episodes 1-8) (2021) WEB-DLRip-AVC Ukr/Eng
                        g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) ?/([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value.Trim();
                            originalname = g[2].Value.Trim();

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Детективне агентство прекрасних хлопчиків (08 з 12) / Bishounen Tanteidan (2021) BDRip 1080p Ukr/Jap | Ukr Sub
                            g = Regex.Match(title, "^([^/\\(\\[]+) (\\(|\\[)[^\\)\\]]+(\\)|\\]) ?/([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                            {
                                name = g[1].Value.Trim();
                                originalname = g[4].Value.Trim();

                                if (int.TryParse(g[5].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Яйця Дракона / Dragon Ball (01-31 з 153) (1986-1989) BDRip 1080p H.265
                                // Томо — дівчина! / Tomo-chan wa Onnanoko! (Сезон 1, серії 01-02 з 13) (2023) WEBDL 1080p H.265 Ukr/Jap | sub Ukr
                                g = Regex.Match(title, "^([^/\\(\\[]+)/([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                {
                                    name = g[1].Value.Trim();
                                    originalname = g[2].Value.Trim();

                                    if (int.TryParse(g[3].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // Людина-бензопила / チェンソーマン /Chainsaw Man (сезон 1, серії 8 з 12) (2022) WEBRip 1080p
                                    g = Regex.Match(title, "^([^/\\(\\[]+)/[^/\\(\\[]+/([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                    {
                                        name = g[1].Value.Trim();
                                        originalname = g[2].Value.Trim();

                                        if (int.TryParse(g[3].Value, out int _yer))
                                            relased = _yer;
                                    }
                                    else
                                    {
                                        // МастерШеф. 10 сезон (1-18 епізоди) (2020) IPTVRip 400p
                                        g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                        {
                                            name = g[1].Value.Trim();

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
                #endregion

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    #region types
                    string[] types = null;
                    switch (cat)
                    {
                        case "16":
                        case "96":
                        case "42":
                            types = new string[] { "movie" };
                            break;
                        case "19":
                        case "139":
                        case "84":
                            types = new string[] { "multfilm" };
                            break;
                        case "32":
                        case "173":
                        case "124":
                            types = new string[] { "serial" };
                            break;
                        case "174":
                        case "44":
                        case "125":
                            types = new string[] { "multserial" };
                            break;
                        case "226":
                        case "227":
                        case "228":
                        case "229":
                        case "230":
                        case "12":
                        case "131":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                        case "127":
                            types = new string[] { "anime" };
                            break;
                        case "132":
                            types = new string[] { "tvshow" };
                            break;
                    }

                    if (types == null)
                        continue;
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    string downloadId = Regex.Match(row, "href=\"download.php\\?id=([0-9]+)\"").Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(downloadId))
                        continue;

                    torrents.Add(new TolokaDetails()
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
                        relased = relased,
                        downloadId = downloadId
                    });
                }
            }

            return torrents;
        }
    }
}
