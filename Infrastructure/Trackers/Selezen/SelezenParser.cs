using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Selezen
{
    public static class SelezenParser
    {
        const string TrackerName = "selezen";

        public static List<TorrentDetails> ParseTorrentsFromListPage(string html)
        {
            var torrents = new List<TorrentDetails>();

            foreach (string row in tParse.ReplaceBadNames(html).Split("card overflow-hidden").Skip(1))
            {
                if (row.Contains(">Аниме</a>"))
                    continue;

                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, @"\s+", " ");
                    return res.Trim();
                }

                if (string.IsNullOrWhiteSpace(row)) continue;

                DateTime createTime = tParse.ParseCreateTime(Match(@"class=""bx bx-calendar""></span>\s*([0-9]{2}\.[0-9]{2}\.[0-9]{4} [0-9]{2}:[0-9]{2})</a>"), "dd.MM.yyyy HH:mm");
                if (createTime == default) continue;

                var g = Regex.Match(row, @"<a href=""(https?://[^""]+)""><h4 class=""card-title"">([^<]+)</h4>").Groups;
                string url = g[1].Value;
                string title = g[2].Value;
                if (string.IsNullOrWhiteSpace(url) || !url.Contains(".html", StringComparison.OrdinalIgnoreCase))
                    continue;

                string _sid = Match(@"<i class=""bx bx-chevrons-up""></i>([0-9 ]+)").Trim();
                string _pir = Match(@"<i class=""bx bx-chevrons-down""></i>([0-9 ]+)").Trim();
                string sizeName = Match(@"<span class=""bx bx-download""></span>([^<]+)</a>").Trim();
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                int relased = 0;
                string name = null, originalname = null;
                g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;
                    if (int.TryParse(g[3].Value, out int _yer)) relased = _yer;
                }
                else
                {
                    g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;
                    originalname = g[2].Value;
                    if (int.TryParse(g[3].Value, out int _yer)) relased = _yer;
                }
                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Тип: мультфильм по жанру в карточке; сериал по [S01]/[01x01-02 из 09] или TVShows в title/url; иначе movie
                string[] types = new string[] { "movie" };
                if (row.Contains(">Мульт") || row.Contains(">мульт"))
                    types = new string[] { "multfilm" };
                else if (title.IndexOf("TVShows", StringComparison.OrdinalIgnoreCase) >= 0
                    || Regex.IsMatch(title, @"\[S\d+\]")
                    || Regex.IsMatch(title, @"\[\d+[xх]\d+")  // 01x01 или 01х01 (латинская/кириллическая х)
                    || (url.IndexOf("tvshows", StringComparison.OrdinalIgnoreCase) >= 0))
                    types = new string[] { "serial" };
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

            return torrents;
        }

        public static string ExtractMagnetFromDetailPage(string fullnews)
        {
            if (fullnews == null) return null;
            return Regex.Match(fullnews, "href=\"(magnet:\\?xt=urn:btih:[^\"]+)\"").Groups[1].Value;
        }
    }
}
