using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Mazepa
{
    public static class MazepaParser
    {
        const string TrackerName = "mazepa";

        public static string CleanTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            string t = title;

            t = Regex.Replace(t, @"\s*\((19|20)\d{2}(\-\d{4})?\)", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(Сезон|Season)\s*\d+.*$", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(S\d{1,2}|E\d{1,2}|S\d{1,2}E\d{1,2})\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(2160p|1080p|720p|480p)\b", "", RegexOptions.IgnoreCase);

            t = Regex.Replace(
                t,
                @"\b(WEB[-\s]?DL|WEB[-\s]?Rip|BDRip|BDRemux|HDRip|BluRay|BRRip|DVDRip|HDTV)\b",
                "",
                RegexOptions.IgnoreCase
            );

            t = Regex.Replace(
                t,
                @"\b(x264|x265|h\.?264|h\.?265|hevc|avc|aac|ac3|dts|ddp?\d\.\d|vc\-?1)\b",
                "",
                RegexOptions.IgnoreCase
            );

            t = Regex.Replace(t, @"[\[\]\|]", " ");
            t = Regex.Replace(t, @"\s{2,}", " ").Trim();

            return t;
        }

        public static (string name, string originalname, int year) ParseNamesAdvanced(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return (null, null, 0);

            var m = Regex.Match(title, @"^(.*?)\s*\((\d{4}|\d{4}-\d{4})\)");
            var yr = Regex.Match(title, @"\((\d{4})\)");
            string beforeYear = m.Success ? m.Groups[1].Value : title;

            var parts = Regex
                .Split(beforeYear, @"\s*/\s*")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList();

            if (parts.Count == 0)
                return (null, null, 0);

            string original = parts.LastOrDefault(p => Regex.IsMatch(p, @"[A-Za-z]"));
            string name = parts.FirstOrDefault(p => !Regex.IsMatch(p, @"[A-Za-z]"));
            int.TryParse(yr.Groups[1].Value, out int year);

            name ??= parts.First();
            original ??= name;

            name = CleanTitle(name);
            original = CleanTitle(original);

            name = tParse.ReplaceBadNames(name) ?? name;
            original = tParse.ReplaceBadNames(original) ?? original;

            return (name, original, year);
        }

        public static DateTime ParseMazepaDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return default;

            text = WebUtility.HtmlDecode(text).Trim();
            text = Regex.Replace(text, @"\s+", " ");

            var m = Regex.Match(text, @"(\d{1,2})\s+([^\s]+)\s+(\d{4}),\s*(\d{1,2}):(\d{2})", RegexOptions.IgnoreCase);
            if (!m.Success)
                return default;

            if (!int.TryParse(m.Groups[1].Value, out int day))
                return default;

            string monthRaw = m.Groups[2].Value.Trim().ToLowerInvariant();
            if (!int.TryParse(m.Groups[3].Value, out int year))
                return default;
            if (!int.TryParse(m.Groups[4].Value, out int hour))
                return default;
            if (!int.TryParse(m.Groups[5].Value, out int minute))
                return default;

            int month = monthRaw switch
            {
                "січ" or "сiч" => 1,
                "лют" => 2,
                "бер" => 3,
                "кві" or "квi" => 4,
                "тра" => 5,
                "чер" => 6,
                "лип" => 7,
                "сер" => 8,
                "вер" => 9,
                "жов" => 10,
                "лис" => 11,
                "гру" => 12,
                _ => 0
            };

            if (month == 0)
                return default;

            try
            {
                return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
            }
            catch
            {
                return default;
            }
        }

        public static string NormalizeMagnet(string magnet)
        {
            if (string.IsNullOrEmpty(magnet)) return null;
            magnet = WebUtility.HtmlDecode(magnet);

            var m = Regex.Match(magnet, @"btih:([A-Fa-f0-9]{40}|[A-Z2-7]{32})");
            if (!m.Success) return null;

            return $"magnet:?xt=urn:btih:{m.Groups[1].Value}";
        }

        public static string ParseSizeName(string block)
        {
            var m = Regex.Match(block, @">([\d\.,]+)\s*&nbsp;(GB|MB|TB)<", RegexOptions.IgnoreCase);
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                return $"{m.Groups[1].Value.Trim()} {m.Groups[2].Value.Trim()}";

            m = Regex.Match(block, @"([\d\.,]+)\s*(GB|MB|TB|ГБ|МБ|ТБ)\b", RegexOptions.IgnoreCase);
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                return $"{m.Groups[1].Value.Trim()} {m.Groups[2].Value.Trim()}";

            return null;
        }

        public static double ParseSizeBytes(string sizeName)
        {
            if (string.IsNullOrWhiteSpace(sizeName)) return 0;
            try
            {
                var g = Regex.Match(sizeName, "([0-9\\.,]+)\\s*(Mb|МБ|GB|ГБ|TB|ТБ)", RegexOptions.IgnoreCase).Groups;
                if (string.IsNullOrWhiteSpace(g[2].Value)) return 0;
                if (!double.TryParse(g[1].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double size))
                    return 0;
                string u = g[2].Value.ToLowerInvariant();
                if (u is "gb" or "гб") size *= 1024;
                else if (u is "tb" or "тб") size *= 1048576;
                return size * 1048576;
            }
            catch { return 0; }
        }

        public static int ParseQuality(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return 480;
            if (title.Contains("2160p") || Regex.IsMatch(title, "(4k|uhd)", RegexOptions.IgnoreCase)) return 2160;
            if (title.Contains("1080p")) return 1080;
            if (title.Contains("720p")) return 720;
            return 480;
        }

        public static string ParseVideotype(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "sdr";
            string lower = title.ToLower();
            if (Regex.IsMatch(lower, @"\bhdr\b|hdr10")) return "hdr";
            return "sdr";
        }

        public static List<TorrentDetails> ParseTorrentsFromCategoryPage(string html, string[] types, string host)
        {
            var list = new List<TorrentDetails>();

            var rows = Regex.Matches(html, @"<tr id=""tr-(\d+)"".*?>.*?</tr>", RegexOptions.Singleline);
            if (rows.Count == 0) return list;

            foreach (Match row in rows)
            {
                string block = row.Value;

                string tid = Regex.Match(block, @"tr-(\d+)").Groups[1].Value;
                if (string.IsNullOrEmpty(tid)) continue;

                string title = Regex.Match(block, @"class=""torTopic[^""]*""><b>([^<]+)</b>").Groups[1].Value;
                string magnet = Regex.Match(block, @"href=""(magnet:\?[^""]+)""").Groups[1].Value;

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(magnet))
                    continue;

                string sizeName = ParseSizeName(block);

                int.TryParse(Regex.Match(block, @"seedmed[^>]*><b>(\d+)</b>").Groups[1].Value, out int sid);
                int.TryParse(Regex.Match(block, @"leechmed[^>]*><b>(\d+)</b>").Groups[1].Value, out int pir);

                string lastPostText = Regex.Match(
                    block,
                    @"<ul class=""last_post[^""]*"">.*?<a[^>]*>([^<]+)</a>",
                    RegexOptions.Singleline
                ).Groups[1].Value;

                DateTime lastPostTime = ParseMazepaDate(lastPostText);
                if (lastPostTime == default)
                    lastPostTime = DateTime.UtcNow;

                var titleTrim = title.Trim();
                var (name, originalname, year) = ParseNamesAdvanced(titleTrim);

                list.Add(new TorrentDetails()
                {
                    trackerName = TrackerName,
                    types = types,
                    url = $"{host}/viewtopic.php?t={tid}",
                    title = titleTrim,
                    name = name,
                    originalname = originalname,
                    magnet = NormalizeMagnet(magnet),
                    sizeName = sizeName,
                    size = ParseSizeBytes(sizeName),
                    quality = ParseQuality(titleTrim),
                    videotype = ParseVideotype(titleTrim),
                    sid = sid,
                    pir = pir,
                    createTime = lastPostTime,
                    updateTime = lastPostTime,
                    relased = year
                });
            }

            return list.GroupBy(x => x.url).Select(g => g.First()).ToList();
        }
    }
}
