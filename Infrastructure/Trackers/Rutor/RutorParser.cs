using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Rutor
{
    public static class RutorParser
    {
        const string TrackerName = "rutor";

        public static List<TorrentBaseDetails> ParseTorrentsFromPage(string html, string cat)
        {
            var torrents = new List<TorrentBaseDetails>();

            if (!RutorCategories.Map.TryGetValue(cat, out var meta))
                return torrents;

            foreach (string row in Regex.Split(Regex.Replace(html, "[\n\r\t]+", ""), "<tr class=\"(gai|tum)\">").Skip(1))
            {
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Replace(" ", " ").Trim();
                }

                if (string.IsNullOrWhiteSpace(row) || !row.Contains("magnet:?xt=urn"))
                    continue;

                DateTime createTime = tParse.ParseCreateTime(Match("<td>([^<]+)</td><td([^>]+)?><a class=\"downgif\""), "dd.MM.yy");
                if (createTime == default)
                    continue;

                string url = Match("<a href=\"/(torrent/[^\"]+)\">");
                string title = Match("<a href=\"/torrent/[^\"]+\">([^<]+)</a>");
                string _sid = Match("<span class=\"green\"><img [^>]+>&nbsp;([0-9]+)</span>");
                string _pir = Match("<span class=\"red\">&nbsp;([0-9]+)</span>");
                string sizeName = Match("<td align=\"right\">([^<]+)</td>");
                string magnet = Match("href=\"(magnet:\\?xt=[^\"]+)\"");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || title.ToLower().Contains("трейлер") || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName) || string.IsNullOrWhiteSpace(magnet))
                    continue;

                if (meta.RequireUkrInTitle && !title.Contains(" UKR"))
                    continue;

                if (title.Contains(" КПК"))
                    continue;

                url = $"{AppInit.conf.Rutor.host}/{url}";

                var (name, originalname, relased) = ParseTitleNames(meta.TitleKind, title);

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int.TryParse(_sid, out int sid);
                int.TryParse(_pir, out int pir);

                torrents.Add(new TorrentBaseDetails()
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

        static (string name, string originalname, int relased) ParseTitleNames(RutorTitleKind titleKind, string title)
        {
            return titleKind switch
            {
                RutorTitleKind.ForeignMovie => ParseForeignMovieTitle(title),
                RutorTitleKind.RuMovie => ParseRuMovieTitle(title),
                RutorTitleKind.ForeignSerial => ParseForeignSerialTitle(title),
                RutorTitleKind.RuSerial => ParseRuSerialTitle(title),
                RutorTitleKind.ShowLike => ParseShowLikeTitle(title),
                _ => (null, null, 0)
            };
        }

        static (string name, string originalname, int relased) ParseForeignMovieTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

            var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value;
                originalname = g[3].Value;

                if (int.TryParse(g[4].Value, out int _yer))
                    relased = _yer;
            }
            else
            {
                g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;

                name = g[1].Value;
                originalname = g[2].Value;

                if (int.TryParse(g[3].Value, out int _yer))
                    relased = _yer;
            }

            return (name, originalname, relased);
        }

        static (string name, string originalname, int relased) ParseRuMovieTitle(string title)
        {
            int relased = 0;
            var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;
            string name = g[1].Value;

            if (int.TryParse(g[2].Value, out int _yer))
                relased = _yer;

            return (name, null, relased);
        }

        static (string name, string originalname, int relased) ParseForeignSerialTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

            var g = Regex.Match(title, "^([^/]+) / [^/]+ / [^/]+ / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value;
                originalname = g[2].Value;

                if (int.TryParse(g[3].Value, out int _yer))
                    relased = _yer;
            }
            else
            {
                g = Regex.Match(title, "^([^/]+) / [^/]+ / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
                else
                {
                    g = Regex.Match(title, "^([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;

                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
            }

            return (name, originalname, relased);
        }

        static (string name, string originalname, int relased) ParseRuSerialTitle(string title)
        {
            int relased = 0;
            var g = Regex.Match(title, "^([^/]+) \\[[^\\]]+\\] \\(([0-9]{4})(\\)|-)").Groups;
            string name = g[1].Value;

            if (int.TryParse(g[2].Value, out int _yer))
                relased = _yer;

            return (name, null, relased);
        }

        static (string name, string originalname, int relased) ParseShowLikeTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

            if (title.Contains(" / "))
            {
                if (title.Contains("[") && title.Contains("]"))
                {
                    var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        g = Regex.Match(title, "^([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;

                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                }
                else
                {
                    var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;

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
                    var g = Regex.Match(title, "^([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                    name = g[1].Value;

                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                }
                else
                {
                    var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;

                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                }
            }

            return (name, originalname, relased);
        }
    }
}
