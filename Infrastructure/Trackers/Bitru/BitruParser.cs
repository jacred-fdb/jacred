using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Bitru
{
    public static class BitruParser
    {
        public static async Task<bool> ParsePageAsync(string cat, int page)
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
    }
}
