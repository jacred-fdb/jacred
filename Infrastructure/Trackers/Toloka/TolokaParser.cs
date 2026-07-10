using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Toloka
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

                if (!TryParseCreateTime(row, out DateTime createTime))
                    continue;

                if (!TryParseRowFields(row, out string url, out string title, out string sid, out string pir, out string sizeName))
                    continue;

                var (name, originalname, relased) = ParseTitleNames(cat, title);

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string[] types = GetTypesForCategory(cat);
                if (types == null)
                    continue;

                int.TryParse(sid, out int sidNum);
                int.TryParse(pir, out int pirNum);

                string downloadId = Regex.Match(row, "href=\"download.php\\?id=([0-9]+)\"").Groups[1].Value;
                if (string.IsNullOrWhiteSpace(downloadId))
                    continue;

                torrents.Add(new TolokaDetails()
                {
                    trackerName = TrackerName,
                    types = types,
                    url = url,
                    title = title,
                    sid = sidNum,
                    pir = pirNum,
                    sizeName = sizeName,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    relased = relased,
                    downloadId = downloadId
                });
            }

            return torrents;
        }

        static string MatchRow(string row, string pattern, int index = 1)
        {
            string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
            res = Regex.Replace(res, "[\n\r\t ]+", " ");
            return res.Trim();
        }

        static bool TryParseCreateTime(string row, out DateTime createTime)
        {
            string raw = MatchRow(row, "class=\"postdetails\">([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})").Replace("-", ".");
            if (!DateTime.TryParse(raw, out createTime) || createTime == default)
            {
                createTime = default;
                return false;
            }

            return true;
        }

        static bool TryParseRowFields(string row, out string url, out string title, out string sid, out string pir, out string sizeName)
        {
            url = MatchRow(row, "<a href=\"(t[0-9]+)\" class=\"topictitle\"");
            title = MatchRow(row, "class=\"topictitle\">([^<]+)</a>");
            sid = MatchRow(row, "<span class=\"seedmed\" [^>]+><b>([0-9]+)</b></span>");
            pir = MatchRow(row, "<span class=\"leechmed\" [^>]+><b>([0-9]+)</b></span>");
            sizeName = MatchRow(row, "<a href=\"download.php[^\"]+\" [^>]+>([^<]+)</a>").Replace("&nbsp;", " ");

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(pir) || string.IsNullOrWhiteSpace(sizeName) || sizeName == "0 B")
            {
                url = title = sid = pir = sizeName = null;
                return false;
            }

            url = $"{AppInit.conf.Toloka.host}/{url}";
            return true;
        }

        static (string name, string originalname, int relased) ParseTitleNames(string cat, string title)
        {
            if (cat is "16" or "96" or "19" or "139" or "12" or "131" or "84" or "42")
                return ParseMovieTitle(title);

            if (cat is "32" or "173" or "174" or "44" or "230" or "226" or "227" or "228" or "229" or "127" or "124" or "125" or "132")
                return ParseSerialTitle(title);

            return (null, null, 0);
        }

        static (string name, string originalname, int relased) ParseMovieTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

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
                    g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                    {
                        name = g[1].Value;
                        if (int.TryParse(g[2].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
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

            return (name, originalname, relased);
        }

        static (string name, string originalname, int relased) ParseSerialTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

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

            return (name, originalname, relased);
        }

        static string[] GetTypesForCategory(string cat)
        {
            switch (cat)
            {
                case "16":
                case "96":
                case "42":
                    return new string[] { "movie" };
                case "19":
                case "139":
                case "84":
                    return new string[] { "multfilm" };
                case "32":
                case "173":
                case "124":
                    return new string[] { "serial" };
                case "174":
                case "44":
                case "125":
                    return new string[] { "multserial" };
                case "226":
                case "227":
                case "228":
                case "229":
                case "230":
                case "12":
                case "131":
                    return new string[] { "docuserial", "documovie" };
                case "127":
                    return new string[] { "anime" };
                case "132":
                    return new string[] { "tvshow" };
                default:
                    return null;
            }
        }
    }
}
