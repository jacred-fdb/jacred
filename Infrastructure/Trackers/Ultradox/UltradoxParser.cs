using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Ultradox
{
    /// <summary>
    /// Parses ultradox.onl (redirects to numbered ultadox.space mirrors).
    /// Listing magnets have empty btih — real magnets live on detail pages.
    /// </summary>
    public static class UltradoxParser
    {
        public const string TrackerName = "ultradox";

        /// <summary>
        /// Search-engine Referer required by the site's nginx gate (own origin → 503).
        /// </summary>
        public const string SearchEngineReferer = "https://www.google.com/";

        static readonly Regex RowSplitRe = new(
            @"<tr>\s*<td class=""torrent-table-date"">",
            RegexOptions.Compiled);

        static readonly Regex RowDateRe = new(@"^([^<]+)</td>", RegexOptions.Compiled);

        static readonly Regex RowTimeRe = new(@"([0-9]{2}):([0-9]{2})", RegexOptions.Compiled);

        static readonly Regex RowDetailLinkRe = new(
            @"<td class=""torrent-table-href"">\s*<a[^>]+href=""([^""#]+)""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex RowImdbRe = new(
            @"<span\s+data-clipboard-text=""https://www\.imdb\.com/title/(tt[0-9]+)/?""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex RowSpanQualityRe = new(
            @"<span[^>]*style=""font-weight:\s*bold;?""[^>]*>([\s\S]*?)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex RowTagsRe = new(@"<[^>]+>", RegexOptions.Compiled);

        static readonly Regex DetailMagnetRe = new(
            @"magnet:\?xt=urn:btih:([A-Fa-f0-9]+)&xl=([0-9]+)&dn=([^&""<\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex PageNumRe = new(@"/page/([0-9]+)/", RegexOptions.Compiled);

        static readonly Regex TitleYearRe = new(@"\(([0-9]{4})\)", RegexOptions.Compiled);

        static readonly Regex TitleNameRe = new(@"^([^(\[]+)", RegexOptions.Compiled);

        static readonly Regex TitleSeasonRe = new(
            @"\(\s*\d+\s*(?:-\s*\d+\s*)?сезон",
            RegexOptions.Compiled);

        static readonly Regex DetailYearRe = new(
            @"itemprop=""copyrightYear""[^>]*>\s*<span>[^<]*</span>\s*([0-9]{4})",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex DnYearRe = new(@"^(?:19|20)\d{2}", RegexOptions.Compiled);

        static readonly Regex DnSeasonRe = new(
            @"^[Ss]\d{1,2}(?:[Ee]\d{1,3})?$",
            RegexOptions.Compiled);

        static readonly Regex DnResRe = new(@"^\d{3,4}[pP]$", RegexOptions.Compiled);

        static readonly Regex QualityResRe = new(@"([0-9]{3,4})[pP]", RegexOptions.Compiled);

        static readonly Regex StripTagsRe = new(@"<[^>]+>", RegexOptions.Compiled);

        static readonly Regex WhitespaceRe = new(@"\s+", RegexOptions.Compiled);

        static readonly HashSet<string> DnStopTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "bdrip", "webrip", "webdl", "web-dl", "web-dlrip",
            "hdrip", "dvdrip", "camrip", "telecine", "ts",
            "hdtv", "bluray", "proper", "repack",
            "avi", "mkv", "mp4",
            "x264", "x265", "h264", "h265", "hevc", "avc",
        };

        static readonly string[] QualityTags =
        {
            "BDRip", "DVDRip", "HDRip", "WEBRip", "WEB-DL", "CAMRip", "CamRip", "TS"
        };

        static readonly TimeZoneInfo MoscowTz = ResolveMoscowTz();

        public static string ListingUrl(string host, string sectionPath, int page)
        {
            host = (host ?? "").TrimEnd('/');
            sectionPath = (sectionPath ?? "").Trim('/');
            if (page <= 0)
                return $"{host}/{sectionPath}/";
            return $"{host}/{sectionPath}/page/{page}/";
        }

        /// <summary>Highest /page/N/ from listing HTML (at least 1).</summary>
        public static int LastPageFromHtml(string body)
        {
            int maxPage = 1;
            if (string.IsNullOrWhiteSpace(body))
                return maxPage;

            foreach (Match m in PageNumRe.Matches(body))
            {
                if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                    && n > maxPage)
                {
                    maxPage = n;
                }
            }

            return maxPage;
        }

        public static List<UltradoxListingItem> ParseListingHtml(string body)
        {
            var outList = new List<UltradoxListingItem>();
            if (string.IsNullOrWhiteSpace(body))
                return outList;

            string[] chunks = RowSplitRe.Split(body);
            for (int i = 1; i < chunks.Length; i++)
            {
                string row = chunks[i].Trim();
                if (row.Length == 0)
                    continue;

                var dateMatch = RowDateRe.Match(row);
                if (!dateMatch.Success)
                    continue;

                DateTime createTime = ParseRowDate(dateMatch.Groups[1].Value.Trim());
                if (createTime == default)
                    continue;

                var linkMatch = RowDetailLinkRe.Match(row);
                if (!linkMatch.Success)
                    continue;

                string detailUrl = WebUtility.HtmlDecode(linkMatch.Groups[1].Value)?.Trim() ?? "";
                if (detailUrl.Length == 0)
                    continue;

                string title = FlattenTitle(linkMatch.Groups[2].Value);
                if (title.Length == 0)
                    continue;

                string imdb = "";
                var imdbMatch = RowImdbRe.Match(row);
                if (imdbMatch.Success)
                    imdb = imdbMatch.Groups[1].Value;

                outList.Add(new UltradoxListingItem
                {
                    CreateTime = createTime,
                    DetailUrl = detailUrl,
                    Title = title,
                    Imdb = imdb
                });
            }

            return outList;
        }

        /// <summary>
        /// Absolute "DD-MM-YYYY, HH:MM", "Сегодня, HH:MM", or "Вчера, HH:MM" (Europe/Moscow → UTC).
        /// </summary>
        public static DateTime ParseRowDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return default;

            s = s.Trim();
            if (DateTime.TryParseExact(
                    s, "dd-MM-yyyy, HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime absolute))
            {
                var local = DateTime.SpecifyKind(absolute, DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTimeToUtc(local, MoscowTz);
            }

            int relativeDays;
            if (s.StartsWith("Сегодня", StringComparison.Ordinal))
                relativeDays = 0;
            else if (s.StartsWith("Вчера", StringComparison.Ordinal))
                relativeDays = -1;
            else
                return default;

            var hm = RowTimeRe.Match(s);
            if (!hm.Success)
                return default;

            int.TryParse(hm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hour);
            int.TryParse(hm.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minute);

            DateTime nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MoscowTz);
            DateTime day = nowLocal.Date.AddDays(relativeDays);
            try
            {
                var local = new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTimeToUtc(local, MoscowTz);
            }
            catch (ArgumentOutOfRangeException)
            {
                return default;
            }
        }

        public static bool TryParseDetailHtml(
            string body, out List<UltradoxMagnetVariant> variants, out UltradoxDetailInfo info)
        {
            variants = new List<UltradoxMagnetVariant>();
            info = new UltradoxDetailInfo();
            if (string.IsNullOrWhiteSpace(body))
                return false;

            var yearMatch = DetailYearRe.Match(body);
            if (yearMatch.Success
                && int.TryParse(yearMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year))
            {
                info.Year = year;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in DetailMagnetRe.Matches(body))
            {
                string hash = m.Groups[1].Value.ToLowerInvariant();
                if (!seen.Add(hash))
                    continue;

                long.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes);
                string dn = m.Groups[3].Value;
                string magnet = ExtractFullMagnet(body, m.Index);
                if (string.IsNullOrEmpty(magnet))
                    magnet = m.Value;

                variants.Add(new UltradoxMagnetVariant
                {
                    Hash = hash,
                    Bytes = bytes,
                    Dn = dn,
                    Magnet = magnet,
                    Quality = ExtractQuality(dn)
                });
            }

            foreach (var v in variants)
            {
                string original = OriginalFromFilename(v.Dn);
                if (!string.IsNullOrEmpty(original))
                {
                    info.Original = original;
                    break;
                }
            }

            return variants.Count > 0;
        }

        public static int ExtractDetailYear(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return 0;
            var m = DetailYearRe.Match(body);
            if (!m.Success)
                return 0;
            int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year);
            return year;
        }

        public static string ExtractQuality(string dn)
        {
            string clean = (dn ?? "").Replace("O", "0");
            var m = QualityResRe.Match(clean);
            if (m.Success)
                return m.Groups[1].Value + "p";

            foreach (string tag in QualityTags)
            {
                if ((dn ?? "").Contains(tag, StringComparison.Ordinal))
                    return tag;
            }

            return "";
        }

        public static (string name, string original, int year) ParseTitle(string title)
        {
            title ??= "";
            string cut = title;
            int year = 0;

            var yearMatch = TitleYearRe.Match(title);
            if (yearMatch.Success)
            {
                int.TryParse(yearMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out year);
                cut = title[..yearMatch.Index];
            }
            else
            {
                cut = TrimMetadataBlocks(title);
            }

            cut = cut.Trim();
            int slash = cut.IndexOf(" / ", StringComparison.Ordinal);
            if (slash >= 0)
                return (cut[..slash].Trim(), cut[(slash + 3)..].Trim(), year);

            return (cut.Trim(), "", year);
        }

        public static string OriginalFromFilename(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn))
                return "";

            string trimmed = dn.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)
                ? dn[..^".torrent".Length]
                : dn;
            string[] tokens = trimmed.Split('.');
            int end = -1;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (IsDnStopToken(tokens[i]))
                {
                    end = i;
                    break;
                }
            }

            if (end <= 0)
                return "";

            var titleTokens = new List<string>(tokens[..end]);
            if (titleTokens.Count > 1)
            {
                string last = titleTokens[^1].ToUpperInvariant();
                if (last is "US" or "UK")
                    titleTokens.RemoveAt(titleTokens.Count - 1);
            }

            string result = string.Join(" ", titleTokens).Trim();
            if (!HasLatin(result))
                return "";
            return result;
        }

        public static TorrentDetails BuildTorrent(
            string host,
            string sectionPath,
            string[] types,
            UltradoxListingItem item,
            UltradoxMagnetVariant variant,
            UltradoxDetailInfo info)
        {
            if (item == null || variant == null
                || string.IsNullOrWhiteSpace(variant.Hash)
                || string.IsNullOrWhiteSpace(variant.Magnet))
            {
                return null;
            }

            info ??= new UltradoxDetailInfo();
            var (name, originalName, year) = ParseTitle(item.Title);
            if (year == 0)
                year = info.Year;

            // rufilm filenames are transliterations, not foreign originals.
            if (string.IsNullOrWhiteSpace(originalName)
                && !string.Equals(sectionPath, "rufilm", StringComparison.OrdinalIgnoreCase))
            {
                originalName = info.Original ?? "";
            }

            string title = (item.Title ?? "").Trim();
            if (!string.IsNullOrEmpty(variant.Quality)
                && title.IndexOf(variant.Quality, StringComparison.OrdinalIgnoreCase) < 0)
            {
                title = title + " [" + variant.Quality + "]";
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                var m = TitleNameRe.Match(title);
                if (m.Success)
                    name = m.Groups[1].Value.Trim();
            }

            if (string.IsNullOrWhiteSpace(name))
                return null;

            host = (host ?? "").TrimEnd('/');
            string detailUrl = item.DetailUrl ?? "";
            if (!detailUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                detailUrl = host + "/" + detailUrl.TrimStart('/');

            string hashPrefix = variant.Hash.Length <= 8 ? variant.Hash : variant.Hash[..8];
            string uniqueUrl = detailUrl + "#h=" + hashPrefix;

            return new TorrentDetails
            {
                trackerName = TrackerName,
                types = types ?? Array.Empty<string>(),
                url = uniqueUrl,
                title = title,
                sid = 1,
                pir = 1,
                sizeName = HumanSize(variant.Bytes),
                magnet = variant.Magnet,
                createTime = item.CreateTime == default ? DateTime.UtcNow : item.CreateTime,
                updateTime = DateTime.UtcNow,
                name = name,
                originalname = originalName ?? "",
                relased = year
            };
        }

        public static string HumanSize(long bytes)
        {
            if (bytes <= 0)
                return "";

            const long kb = 1L << 10;
            const long mb = 1L << 20;
            const long gb = 1L << 30;
            const long tb = 1L << 40;

            if (bytes >= tb)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00} TB", bytes / (double)tb);
            if (bytes >= gb)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00} GB", bytes / (double)gb);
            if (bytes >= mb)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00} MB", bytes / (double)mb);
            if (bytes >= kb)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00} KB", bytes / (double)kb);
            return bytes + " B";
        }

        public static bool ListingMagnetsArePlaceholders(string body) =>
            (body ?? "").Contains("magnet:?xt=urn:btih:&", StringComparison.Ordinal);

        static string FlattenTitle(string raw)
        {
            string span = "";
            var spanMatch = RowSpanQualityRe.Match(raw ?? "");
            if (spanMatch.Success)
                span = spanMatch.Groups[1].Value;

            string mainText = RowTagsRe.Replace(raw ?? "", " ");
            mainText = WebUtility.HtmlDecode(mainText) ?? "";

            if (!string.IsNullOrEmpty(span))
            {
                string spanPlain = WebUtility.HtmlDecode(RowTagsRe.Replace(span, "")) ?? "";
                mainText = mainText.Replace(spanPlain, "");
                mainText = mainText.Trim() + " " + spanPlain;
            }

            return CollapseSpaces(mainText);
        }

        static string TrimMetadataBlocks(string title)
        {
            int end = title.Length;
            var season = TitleSeasonRe.Match(title);
            if (season.Success)
                end = season.Index;

            int bracket = title.IndexOf('[');
            if (bracket >= 0 && bracket < end)
                end = bracket;

            return title[..end];
        }

        static bool IsDnStopToken(string tok)
        {
            string norm = (tok ?? "").Replace("O", "0");
            if (DnYearRe.IsMatch(norm) || DnResRe.IsMatch(norm))
                return true;
            if (DnSeasonRe.IsMatch(tok ?? ""))
                return true;
            return DnStopTokens.Contains(tok ?? "");
        }

        static string ExtractFullMagnet(string body, int start)
        {
            if (start < 0 || start >= body.Length)
                return "";

            int endRel = -1;
            for (int i = start; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '"' || c == '<')
                {
                    endRel = i - start;
                    break;
                }
            }

            if (endRel < 0)
                return "";

            return WebUtility.HtmlDecode(body.Substring(start, endRel)) ?? "";
        }

        static string CollapseSpaces(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            s = StripTagsRe.Replace(s, " ");
            s = WebUtility.HtmlDecode(s) ?? "";
            return WhitespaceRe.Replace(s, " ").Trim();
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

        static TimeZoneInfo ResolveMoscowTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }

            try { return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }

            return TimeZoneInfo.CreateCustomTimeZone("MSK", TimeSpan.FromHours(3), "MSK", "MSK");
        }
    }

    public sealed class UltradoxListingItem
    {
        public DateTime CreateTime { get; set; }
        public string DetailUrl { get; set; }
        public string Title { get; set; }
        public string Imdb { get; set; }
    }

    public sealed class UltradoxDetailInfo
    {
        public int Year { get; set; }
        public string Original { get; set; }
    }

    public sealed class UltradoxMagnetVariant
    {
        public string Hash { get; set; }
        public long Bytes { get; set; }
        public string Dn { get; set; }
        public string Magnet { get; set; }
        public string Quality { get; set; }
    }
}
