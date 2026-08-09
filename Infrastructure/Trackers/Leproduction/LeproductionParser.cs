using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Leproduction
{
    public static class LeproductionParser
    {
        public const string TrackerName = "leproduction";

        static readonly Regex ShortImgRe = new(
            @"<a\s+class=""short-img""\s+href=""(?<url>(?:https?://[^""]+)?/[^""]+?\.html)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex H3LinkRe = new(
            @"<h3>\s*<a\s+href=""(?<url>(?:https?://[^""]+)?/[^""]+?\.html)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex PageNumRe = new(
            @"/page/([0-9]+)/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex NameRuRe = new(
            @"Русское\s+название:\s*</div>\s*<div[^>]*class=""info-desc""[^>]*>\s*([^<]+)\s*</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex NameEnRe = new(
            @"Оригинальное\s+название:\s*</div>\s*<div[^>]*class=""info-desc""[^>]*>\s*([^<]+)\s*</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex H1Re = new(
            @"<h1>([^<]+)</h1>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex YearRe = new(
            @"info-label"">Год выпуска:</div>\s*<div[^>]*class=""info-desc""[^>]*>\s*<a[^>]*>(\d{4})</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex DownloadIdRe = new(
            @"index\.php\?do=download&(?:amp;)?id=(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex TorrentInfoRe = new(
            @"id\s*=\s*""torrent_(\d+)_info""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex MagnetHrefRe = new(
            @"href\s*=\s*""(magnet:[^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex MagnetRawRe = new(
            @"(magnet:[^\s""'<]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex FileNameRe = new(
            @"class=""info_d1-le""[^>]*>\s*([^<]+)\s*</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex SidLeRe = new(
            @"Раздают:\s*</b>\s*<span[^>]*class=""li_distribute_m-le""[^>]*>\s*([0-9]+)\s*</span>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex PirLeRe = new(
            @"Качают:\s*</b>\s*<span[^>]*class=""li_swing_m-le""[^>]*>\s*([0-9]+)\s*</span>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex SizeLeRe = new(
            @"Размер:\s*<span[^>]*>\s*([0-9]+(?:[.,][0-9]+)?)\s*(Mb|Gb|Tb)\s*</span>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex QualityRe = new(
            @"\b([0-9]{3,4}p)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex EpisodeRe = new(
            @"\[([0-9]+(?:\s*[-,]\s*[0-9]+)*\s+из\s+[0-9]+)\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex CleanSpaceRe = new(@"\s+", RegexOptions.Compiled);

        static readonly Regex InlineSlashTailRe = new(@"\s*/\s*.*$", RegexOptions.Compiled);

        public static bool TryGetTypes(string cat, out string[] types)
        {
            types = null;
            if (!LeproductionCategories.Map.TryGetValue(cat ?? "", out var meta) || meta?.Types == null)
                return false;
            types = meta.Types;
            return true;
        }

        public static int DetectLastPage(string html)
        {
            if (string.IsNullOrEmpty(html))
                return 1;

            int maxPage = 1;
            foreach (Match m in PageNumRe.Matches(html))
            {
                if (int.TryParse(m.Groups[1].Value, out int n) && n > maxPage)
                    maxPage = n;
            }

            return maxPage;
        }

        public static List<string> ExtractPostUrls(string html, string host)
        {
            var outList = new List<string>();
            if (string.IsNullOrEmpty(html) || string.IsNullOrWhiteSpace(host))
                return outList;

            host = host.TrimEnd('/');
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddMatches(Regex re)
            {
                foreach (Match m in re.Matches(html))
                {
                    string u = m.Groups["url"].Value;
                    if (string.IsNullOrWhiteSpace(u))
                        continue;
                    if (u.StartsWith('/'))
                        u = host + u;
                    if (seen.Add(u))
                        outList.Add(u);
                }
            }

            AddMatches(ShortImgRe);
            AddMatches(H3LinkRe);
            return outList;
        }

        public static string ExtractMagnet(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var m = MagnetHrefRe.Match(html);
            if (m.Success)
                return WebUtility.HtmlDecode(m.Groups[1].Value);

            m = MagnetRawRe.Match(html);
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        public static string ExtractTorrentId(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;
            var m = Regex.Match(url, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static List<TorrentDetails> ParseDetailHtml(string html, string postUrl, string[] types)
        {
            var outList = new List<TorrentDetails>();
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(postUrl) || types == null || types.Length == 0)
                return outList;

            string nameRu = ExtractMatch(NameRuRe, html);
            string nameEn = ExtractMatch(NameEnRe, html);
            if (string.IsNullOrWhiteSpace(nameRu))
            {
                string h1 = ExtractMatch(H1Re, html);
                if (!string.IsNullOrWhiteSpace(h1))
                    nameRu = InlineSlashTailRe.Replace(h1, "").Trim();
            }

            if (string.IsNullOrWhiteSpace(nameRu))
                return outList;

            int relased = 0;
            var ym = YearRe.Match(html);
            if (ym.Success)
                int.TryParse(ym.Groups[1].Value, out relased);

            string decoded = WebUtility.HtmlDecode(html);
            var ids = UniqueMatches(DownloadIdRe, decoded);
            if (ids.Count == 0)
                ids = UniqueMatches(DownloadIdRe, html);
            if (ids.Count == 0)
                ids = UniqueMatches(TorrentInfoRe, html);
            if (ids.Count == 0)
                return outList;

            var pageMagnets = CollectMagnets(html);
            DateTime now = DateTime.UtcNow;

            for (int i = 0; i < ids.Count; i++)
            {
                string tid = ids[i];
                string around = TakeAround(decoded, "torrent_" + tid + "_info", 20000);
                if (string.IsNullOrEmpty(around))
                    around = TakeAround(html, "torrent_" + tid + "_info", 20000);

                int sid = 0, pir = 0;
                var sm = SidLeRe.Match(around);
                if (sm.Success)
                    int.TryParse(sm.Groups[1].Value, out sid);
                var pm = PirLeRe.Match(around);
                if (pm.Success)
                    int.TryParse(pm.Groups[1].Value, out pir);

                string sizeName = null;
                double sizeBytes = 0;
                var sz = SizeLeRe.Match(around);
                if (sz.Success)
                {
                    string numRaw = sz.Groups[1].Value.Replace(',', '.');
                    string unit = sz.Groups[2].Value;
                    sizeName = numRaw + " " + unit;
                    if (double.TryParse(numRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
                    {
                        switch (unit.ToLowerInvariant())
                        {
                            case "tb":
                                num *= 1024 * 1024;
                                break;
                            case "gb":
                                num *= 1024;
                                break;
                        }

                        sizeBytes = num * 1048576; // MB → bytes
                    }
                }

                string q = null;
                string ep = null;
                string fn = ExtractMatch(FileNameRe, around);
                if (!string.IsNullOrWhiteSpace(fn))
                {
                    var qm = QualityRe.Match(fn);
                    if (qm.Success)
                        q = qm.Groups[1].Value;
                    var em = EpisodeRe.Match(fn);
                    if (em.Success)
                        ep = em.Groups[1].Value;
                }

                string qDigits = "0";
                int quality = 0;
                if (!string.IsNullOrWhiteSpace(q))
                {
                    qDigits = q.ToLowerInvariant().Replace("p", "", StringComparison.Ordinal);
                    int.TryParse(qDigits, out quality);
                }

                string magnet = ExtractMagnet(around);
                if (string.IsNullOrWhiteSpace(magnet) && i < pageMagnets.Count && pageMagnets.Count == ids.Count)
                    magnet = pageMagnets[i];

                string title = nameRu;
                if (!string.IsNullOrWhiteSpace(nameEn))
                    title = nameRu + " / " + nameEn;
                if (!string.IsNullOrWhiteSpace(ep))
                    title += " [" + ep + "]";
                if (relased > 0)
                    title += " " + relased;
                if (!string.IsNullOrWhiteSpace(q))
                    title += " [" + q + "]";

                string url = $"{postUrl}?q={qDigits}&id={tid}";
                string original = string.IsNullOrWhiteSpace(nameEn) ? nameRu : nameEn;

                outList.Add(new TorrentDetails
                {
                    trackerName = TrackerName,
                    types = types,
                    url = url,
                    title = CleanSpaces(title),
                    sid = sid,
                    pir = pir,
                    sizeName = sizeName,
                    size = sizeBytes,
                    createTime = now,
                    updateTime = now,
                    name = nameRu,
                    originalname = original,
                    relased = relased,
                    magnet = magnet,
                    quality = quality
                });
            }

            return outList;
        }

        static string ExtractMatch(Regex re, string body)
        {
            var m = re.Match(body ?? "");
            if (!m.Success || m.Groups.Count < 2)
                return null;
            return CleanSpaces(WebUtility.HtmlDecode(m.Groups[1].Value));
        }

        static List<string> UniqueMatches(Regex re, string body)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var outList = new List<string>();
            if (string.IsNullOrEmpty(body))
                return outList;

            foreach (Match m in re.Matches(body))
            {
                if (m.Groups.Count < 2)
                    continue;
                string v = m.Groups[1].Value;
                if (seen.Add(v))
                    outList.Add(v);
            }

            return outList;
        }

        static List<string> CollectMagnets(string body)
        {
            var outList = new List<string>();
            if (string.IsNullOrEmpty(body))
                return outList;

            foreach (Match m in MagnetHrefRe.Matches(body))
                outList.Add(WebUtility.HtmlDecode(m.Groups[1].Value));
            return outList;
        }

        static string TakeAround(string text, string needle, int radius)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle))
                return "";

            int idx = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return "";

            int s = Math.Max(0, idx - radius);
            int e = Math.Min(text.Length, idx + needle.Length + radius);
            return text.Substring(s, e - s);
        }

        static string CleanSpaces(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return null;
            return CleanSpaceRe.Replace(s.Trim(), " ");
        }
    }
}
