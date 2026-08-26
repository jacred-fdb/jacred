using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Viruseproject
{
    public static class ViruseprojectParser
    {
        public const string TrackerName = "viruseproject";

        static readonly Regex ItemHrefRe = new(
            @"<h3\s+class=""catItemTitle"">\s*<a\s+href=""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex PaginationEndRe = new(
            @"<li\s+class=""pagination-end"">\s*<a[^>]+href=""[^""]*?[?&]start=(\d+)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex ItemTitleRe = new(
            @"<h2\s+class=""itemTitle"">\s*(.+?)\s*</h2>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex ItemDateRe = new(
            @"<span\s+class=""itemDateCreated"">\s*(.+?)\s*</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex ExtraFieldRe = new(
            @"<span\s+class=""itemExtraFieldsLabel"">\s*([^<]+?)\s*</span>\s*<span\s+class=""itemExtraFieldsValue"">\s*([^<]+?)\s*</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex AttachmentRe = new(
            @"<a\s+title=""([^""]+?\.torrent)""\s+href=""([^""]+/download/(\d+)_[a-f0-9]+)""\s*>\s*([^<]+?)\s*</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex YearInTextRe = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);

        static readonly Regex ResolutionRe = new(
            @"\b(2160|1440|1080|720|480|400)p\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex SizeRe = new(
            @"размер\s+([0-9]+(?:[.,][0-9]+)?)\s*(Гб|Мб|Тб|Кб|GB|MB|TB|KB)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex ParenEnRe = new(
            @"^([^()]+?)\s*\(([^()]+)\)\s*$",
            RegexOptions.Compiled);

        static readonly Regex SeasonInfoRe = new(
            @"^сезон\s+\d+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex EpisodeInfoRe = new(
            @"^\d+(\s*[-,]\s*\d+)*\s+из\s+\d+$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex WhitespaceRe = new(@"[\s\u00A0]+", RegexOptions.Compiled);

        static readonly Regex StripTagsRe = new(@"<[^>]+>", RegexOptions.Compiled);

        static readonly (string prefix, int month)[] RussianMonths =
        {
            ("янв", 1), ("фев", 2), ("мар", 3),
            ("апр", 4), ("май", 5), ("мая", 5),
            ("июн", 6), ("июл", 7), ("авг", 8),
            ("сен", 9), ("окт", 10), ("ноя", 11),
            ("дек", 12),
        };

        public static bool TryGetTypes(string cat, out string[] types)
        {
            types = null;
            if (!ViruseprojectCategories.Map.TryGetValue(cat ?? "", out var meta) || meta?.Types == null)
                return false;
            types = meta.Types;
            return true;
        }

        public static int GetPageStep(string cat) =>
            ViruseprojectCategories.Map.TryGetValue(cat ?? "", out var meta) && meta.PageStep > 0
                ? meta.PageStep
                : 10;

        public static int DetectLastPage(string body, int step)
        {
            if (step <= 0 || string.IsNullOrEmpty(body))
                return 1;

            var m = PaginationEndRe.Match(body);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int last) && last > 0)
                return last / step + 1;

            return 1;
        }

        public static List<string> ExtractPostUrls(string body, string host)
        {
            var outList = new List<string>();
            if (string.IsNullOrEmpty(body) || string.IsNullOrWhiteSpace(host))
                return outList;

            host = host.TrimEnd('/');
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in ItemHrefRe.Matches(body))
            {
                string u = WebUtility.HtmlDecode(m.Groups[1].Value)?.Trim();
                if (string.IsNullOrWhiteSpace(u))
                    continue;
                if (u.StartsWith('/'))
                    u = host + u;
                if (seen.Add(u))
                    outList.Add(u);
            }

            return outList;
        }

        public static List<ViruseprojectDetails> ParseDetailHtml(string dhtml, string postUrl, string host, string[] types)
        {
            var outList = new List<ViruseprojectDetails>();
            if (string.IsNullOrWhiteSpace(dhtml) || string.IsNullOrWhiteSpace(postUrl) || types == null || types.Length == 0)
                return outList;

            host = (host ?? "").TrimEnd('/');
            string rawTitle = CleanText(ExtractMatch(ItemTitleRe, dhtml));
            if (string.IsNullOrWhiteSpace(rawTitle))
                return outList;

            var fields = ExtractExtraFields(dhtml);
            int year = 0;
            if (fields.TryGetValue("Год выпуска", out string yearStr) &&
                int.TryParse(yearStr?.Trim(), out int y))
            {
                year = y;
            }

            fields.TryGetValue("Качество видео", out string videoQuality);
            videoQuality = videoQuality?.Trim();

            DateTime createTime = ParseRussianDate(CleanText(ExtractMatch(ItemDateRe, dhtml)));
            var (nameRu, nameEn) = ParseNames(rawTitle);
            bool titleHasYear = YearInTextRe.IsMatch(rawTitle);

            string baseTitle = rawTitle;
            if (!titleHasYear && year > 0)
                baseTitle = $"{baseTitle} ({year})";
            if (!string.IsNullOrWhiteSpace(videoQuality))
                baseTitle = $"{baseTitle} [{videoQuality}]";

            foreach (Match att in AttachmentRe.Matches(dhtml))
            {
                string fileTitle = att.Groups[1].Value.Trim();
                string downloadUrl = WebUtility.HtmlDecode(att.Groups[2].Value)?.Trim() ?? "";
                if (downloadUrl.StartsWith('/'))
                    downloadUrl = host + downloadUrl;
                string downloadId = att.Groups[3].Value.Trim();
                string linkText = CleanText(att.Groups[4].Value);

                int resInt = 400;
                string resolution = "400p";
                var rm = ResolutionRe.Match(fileTitle);
                if (rm.Success && int.TryParse(rm.Groups[1].Value, out int parsedRes))
                {
                    resInt = parsedRes;
                    resolution = parsedRes + "p";
                }

                string sizeName = null;
                double sizeBytes = 0;
                var sm = SizeRe.Match(linkText ?? "");
                if (sm.Success)
                {
                    string numRaw = sm.Groups[1].Value.Replace(',', '.');
                    string unit = sm.Groups[2].Value;
                    sizeName = numRaw + " " + unit;
                    if (double.TryParse(numRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
                    {
                        switch (unit.ToLowerInvariant())
                        {
                            case "тб":
                            case "tb":
                                num *= 1024 * 1024;
                                break;
                            case "гб":
                            case "gb":
                                num *= 1024;
                                break;
                            case "кб":
                            case "kb":
                                num /= 1024;
                                break;
                        }

                        sizeBytes = num * 1048576;
                    }
                }

                string title = $"{baseTitle} [{resolution}]";
                string recordUrl = $"{postUrl}#q={resInt}&id={downloadId}";
                string original = string.IsNullOrWhiteSpace(nameEn) ? nameRu : nameEn;

                outList.Add(new ViruseprojectDetails
                {
                    trackerName = TrackerName,
                    types = types,
                    url = recordUrl,
                    title = title,
                    sid = 1, // site doesn't expose peer counts
                    sizeName = sizeName,
                    size = sizeBytes,
                    createTime = createTime,
                    updateTime = createTime,
                    name = nameRu,
                    originalname = original,
                    relased = year,
                    quality = resInt,
                    videotype = videoQuality,
                    downloadUri = downloadUrl
                });
            }

            return outList;
        }

        public static (string nameRu, string nameEn) ParseNames(string rawTitle)
        {
            rawTitle = rawTitle?.Trim() ?? "";
            if (rawTitle.Length == 0)
                return ("", "");

            var paren = ParenEnRe.Match(rawTitle);
            if (paren.Success)
            {
                string ru = paren.Groups[1].Value.Trim();
                string en = paren.Groups[2].Value.Trim();
                if (HasCyrillic(ru) && HasLatin(en) && !en.Contains('/'))
                    return (ru, en);
            }

            if (!rawTitle.Contains('/'))
                return (rawTitle, rawTitle);

            string[] parts = rawTitle.Split('/');
            var clean = new List<string>();
            foreach (string part in parts)
            {
                string pt = part.Trim();
                if (pt.Length == 0 || IsYearOnly(pt) || SeasonInfoRe.IsMatch(pt) || EpisodeInfoRe.IsMatch(pt))
                    continue;
                clean.Add(pt);
            }

            if (clean.Count == 0)
                return (rawTitle, rawTitle);

            string nameRu = clean[0];
            string nameEn = nameRu;
            for (int i = 1; i < clean.Count; i++)
            {
                if (HasLatin(clean[i]) && !HasCyrillic(clean[i]))
                {
                    nameEn = clean[i];
                    break;
                }
            }

            return (nameRu, nameEn);
        }

        public static DateTime ParseRussianDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return DateTime.UtcNow;

            int comma = s.IndexOf(',');
            if (comma >= 0)
                s = s[(comma + 1)..];
            s = s.Trim();
            string[] parts = WhitespaceRe.Split(s);
            if (parts.Length < 3)
                return DateTime.UtcNow;

            if (!int.TryParse(parts[0], out int day))
                return DateTime.UtcNow;

            int month = MonthFromRussian(parts[1]);
            if (month == 0)
                return DateTime.UtcNow;

            if (!int.TryParse(parts[2], out int year))
                return DateTime.UtcNow;

            int hour = 0, minute = 0;
            if (parts.Length >= 4)
            {
                string[] hm = parts[3].Split(':', 2);
                if (hm.Length == 2)
                {
                    int.TryParse(hm[0], out hour);
                    int.TryParse(hm[1], out minute);
                }
            }

            try
            {
                return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.UtcNow;
            }
        }

        static int MonthFromRussian(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            foreach (var rm in RussianMonths)
            {
                if (s.StartsWith(rm.prefix, StringComparison.Ordinal))
                    return rm.month;
            }

            return 0;
        }

        static Dictionary<string, string> ExtractExtraFields(string body)
        {
            var outMap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(body))
                return outMap;

            foreach (Match m in ExtraFieldRe.Matches(body))
            {
                string label = CleanText(m.Groups[1].Value);
                if (label.EndsWith(':'))
                    label = label[..^1].Trim();
                string value = CleanText(m.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(label))
                    outMap[label] = value;
            }

            return outMap;
        }

        static string ExtractMatch(Regex re, string body)
        {
            var m = re.Match(body ?? "");
            if (!m.Success || m.Groups.Count < 2)
                return "";
            return WebUtility.HtmlDecode(m.Groups[1].Value)?.Trim() ?? "";
        }

        static string CleanText(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            s = StripTagsRe.Replace(s, "");
            s = WebUtility.HtmlDecode(s);
            s = WhitespaceRe.Replace(s, " ");
            return s.Trim();
        }

        static bool IsYearOnly(string s)
        {
            s = s?.Trim() ?? "";
            if (s.Length != 4)
                return false;
            return int.TryParse(s, out int n) && n >= 1900 && n <= 2100;
        }

        static bool HasCyrillic(string s)
        {
            foreach (char r in s ?? "")
            {
                if ((r >= 'А' && r <= 'я') || r == 'Ё' || r == 'ё')
                    return true;
            }

            return false;
        }

        static bool HasLatin(string s)
        {
            foreach (char r in s ?? "")
            {
                if ((r >= 'A' && r <= 'Z') || (r >= 'a' && r <= 'z'))
                    return true;
            }

            return false;
        }
    }
}
