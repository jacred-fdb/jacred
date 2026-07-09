using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using JacRed.Engine;
using JacRed.Engine.CORE;
using JacRed.Engine.Parsing;
using JacRed.Models.Details;

namespace JacRed.Engine.Trackers.Megapeer
{
    public static class MegapeerParser
    {
        const string BrowsePageValidMarker = "id=\"logo\"";

        static readonly int[] ParseDelayCycleMs = { 30_000, 60_000, 90_000 };
        static int _parseDelayIndex;
        static readonly SemaphoreSlim _browseLock = new SemaphoreSlim(1, 1);

        static int GetNextParseDelayMs()
        {
            int i = Interlocked.Increment(ref _parseDelayIndex) - 1;
            return ParseDelayCycleMs[Math.Abs(i % ParseDelayCycleMs.Length)];
        }

        public static async Task<string> GetMegapeerBrowsePage(string url, string cat)
        {
            await _browseLock.WaitAsync();
            try
            {
                var headers = new List<(string name, string val)>()
                {
                    ("dnt", "1"),
                    ("pragma", "no-cache"),
                    ("referer", $"{AppInit.conf.Megapeer.rqHost()}/cat/{cat}"),
                    ("sec-fetch-dest", "document"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "same-origin"),
                    ("sec-fetch-user", "?1"),
                    ("upgrade-insecure-requests", "1")
                };
                const int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    int delayMs = GetNextParseDelayMs();
                    await Task.Delay(delayMs);

                    var (content, response) = await HttpClient.BaseGetAsync(url, encoding: Encoding.GetEncoding(1251), useproxy: AppInit.conf.Megapeer.useproxy, addHeaders: headers);

                    if (!string.IsNullOrEmpty(content) && content.Contains(BrowsePageValidMarker))
                        return content;

                    var status = response?.StatusCode;
                    if (attempt < maxRetries)
                    {
                        ParserLog.Write("megapeer", $"Rate limit or invalid page (status={(int)(status ?? 0)}), retry {attempt}/{maxRetries} after next cycle delay (15/30/45s)");
                        continue;
                    }
                    return null;
                }
                return null;
            }
            finally
            {
                _browseLock.Release();
            }
        }

        public static async Task<bool> ParsePageAsync(string cat, int page)
        {
            string html = await GetMegapeerBrowsePage($"{AppInit.conf.Megapeer.rqHost()}/browse.php?cat={cat}&page={page}", cat);

            if (html == null || !html.Contains(BrowsePageValidMarker))
                return false;

            var torrents = new List<MegapeerDetails>();

            foreach (string row in html.Split("class=\"table_fon\"").Skip(1))
            {
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Replace(" ", " ").Trim();
                }

                DateTime createTime = tParse.ParseCreateTime(Match("<td>([0-9]+ [^ ]+ [0-9]+)</td><td>"), "dd.MM.yy");
                if (createTime == default)
                    continue;

                string url = Match("href=\"/(torrent/[0-9]+)");
                string title = Match("class=\"url\"[^>]*>([^<]+)</a>", 1);
                if (string.IsNullOrWhiteSpace(title))
                    title = Match("class=\"url\">([^<]+)</a></td>");

                string sizeName = Match("<td align=\"right\">([^<\n\r]+)", 1).Trim();

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                string _sid = Match("alt=\"S\"><font [^>]+>([0-9]+)</font>", 1);
                if (string.IsNullOrWhiteSpace(_sid))
                    _sid = Match("alt=\"S\"[^>]*>\\s*([0-9]+)", 1);
                string _pir = Match("alt=\"L\"><font [^>]+>([0-9]+)</font>", 1);
                if (string.IsNullOrWhiteSpace(_pir))
                    _pir = Match("alt=\"L\"[^>]*>\\s*([0-9]+)", 1);

                url = $"{AppInit.conf.Megapeer.host}/{url}";

                int relased = 0;
                string name = null, originalname = null;

                if (cat == "80")
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
                else if (cat == "79")
                {
                    var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;
                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                }
                else if (cat == "6")
                {
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
                }
                else if (cat == "5")
                {
                    var g = Regex.Match(title, "^([^/]+) \\[[^\\]]+\\] \\(([0-9]{4})(\\)|-)").Groups;
                    name = g[1].Value;
                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                }
                else if (cat == "55" || cat == "57" || cat == "76")
                {
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
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    string[] types = Array.Empty<string>();
                    switch (cat)
                    {
                        case "80":
                        case "79":
                            types = new[] { "movie" };
                            break;
                        case "6":
                        case "5":
                            types = new[] { "serial" };
                            break;
                        case "55":
                            types = new[] { "docuserial", "documovie" };
                            break;
                        case "57":
                            types = new[] { "tvshow" };
                            break;
                        case "76":
                            types = new[] { "multfilm", "multserial" };
                            break;
                    }

                    string downloadid = Match("href=\"/?download/([0-9]+)\"");
                    if (string.IsNullOrWhiteSpace(downloadid))
                        continue;

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new MegapeerDetails
                    {
                        trackerName = "megapeer",
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
                        downloadId = downloadid
                    });
                }
            }

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                byte[] _t = await HttpClient.Download($"{AppInit.conf.Megapeer.host}/download/{t.downloadId}", referer: AppInit.conf.Megapeer.host);
                string magnet = BencodeTo.Magnet(_t);

                if (!string.IsNullOrWhiteSpace(magnet))
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
