using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Models.Details;
using JacRed.Models.tParse;

namespace JacRed.Engine.Trackers.Knaben
{
    public static class KnabenParser
    {
        const string TrackerName = "knaben";

        /// <summary>Strips metadata for search key. Series: text before S01E05. Movies: year + metadata.</summary>
        public static string CleanTitleForSearch(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;
            string t = title.Trim();

            t = Regex.Replace(t, @"\[[^\]]*\]", " ");
            var seriesMatch = Regex.Match(t, @"^(.+?)\s+S\d{1,2}E\d{1,2}\b", RegexOptions.IgnoreCase);
            if (seriesMatch.Success && seriesMatch.Groups[1].Length > 0)
                t = seriesMatch.Groups[1].Value.Trim();
            else
            {
                var yearMatch = Regex.Match(t, @"[\(\[](\d{4})[\)\]]");
                if (yearMatch.Success && yearMatch.Index > 0)
                    t = t.Substring(0, yearMatch.Index);
                t = Regex.Replace(t, @"\b(S\d{1,2}E\d{1,2}|S\d{1,2}E?\d{0,2}|E\d{1,2}|\d{1,2}x\d{1,2})\b", "", RegexOptions.IgnoreCase);
                t = Regex.Replace(t, @"\b(Сезон|Season)\s*\d{1,2}(?!\d).*$", "", RegexOptions.IgnoreCase);
            }

            t = Regex.Replace(t, @"\b(2160p|1080p|720p|480p)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(HDR10?|DV|HDR|SDR|10bit)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(WEB[-\s]?DL|WEB[-\s]?Rip|WEB\b|BDRip|BDRemux|HDRip|BluRay|BRRip|DVDRip|HDTV)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(x264|x265|xvid|h\.?264|h\.?265|hevc|avc|aac|ac3|dts)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(AMZN|NF|DS4K|DD\s*5\s*1|DD5\.?1|DDPA|DDP5\.?1|Atmos|DDP?\s*5\.?1|playWEB)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(ESub|Sub)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\.", " ");
            t = Regex.Replace(t, @"[\[\]\|]", " ");
            t = Regex.Replace(t, @"\s{2,}", " ").Trim().TrimEnd(' ', '/', '-', '|');
            t = Regex.Replace(t, @"[.\s]+-\s*[A-Za-z0-9][A-Za-z0-9.-]*$", "", RegexOptions.IgnoreCase);
            t = t.Trim().TrimEnd(' ', '-');
            return string.IsNullOrWhiteSpace(t) ? title : t;
        }

        /// <summary>Extracts name + year. Supports (2026), [2026, ...], standalone 2026. Used by DevController.FixKnabenNames.</summary>
        public static (string name, int relased) ParseNameAndYear(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return (null, 0);
            string name = Regex.Replace(title.Trim(), @"\s+\|\s+[^|]+$", "").Trim();
            if (string.IsNullOrWhiteSpace(name)) return (null, 0);
            int relased = 0;

            var m = Regex.Match(name, @"[\(\[](\d{4})[\)\],\s]");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int y))
            {
                relased = y;
                if (m.Index > 0) name = name.Substring(0, m.Index).TrimEnd(' ', '/', '-', '|');
            }
            else
            {
                var yearMatch = Regex.Match(name, @"\b(19|20)\d{2}\b");
                if (yearMatch.Success && int.TryParse(yearMatch.Value, out int y2))
                {
                    relased = y2;
                    name = Regex.Replace(name, @"\b(19|20)\d{2}\b", "").Trim();
                }
            }
            name = CleanTitleForSearch(name);
            return (string.IsNullOrWhiteSpace(name) ? title.Trim() : name, relased);
        }

        /// <summary>Normalizes title for FileDB: 2160p lowercase, .HDR→ HDR, Dolby Vision→ HDR. Used by DevController.FixKnabenNames.</summary>
        public static string BuildTitleForFileDB(string originalTitle)
        {
            if (string.IsNullOrWhiteSpace(originalTitle)) return originalTitle;
            string t = originalTitle.Trim();
            t = Regex.Replace(t, @"\b2160p\b", "2160p", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b1080p\b", "1080p", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b720p\b", "720p", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\.(HDR10?)\b", " $1", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(t, @"(dolby\s*vision|10-?bit)", RegexOptions.IgnoreCase) && !Regex.IsMatch(t, @"(\.|\[|,| )hdr", RegexOptions.IgnoreCase))
                t += " HDR";
            return t;
        }

        public static TorrentDetails MapToTorrentDetails(KnabenHit h)
        {
            if (string.IsNullOrWhiteSpace(h.Title)) return null;
            var types = GetTypesFromCategoryId(h.CategoryId);
            if (types == null) return null;

            string url = !string.IsNullOrWhiteSpace(h.Details) ? h.Details : h.Link;
            if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(h.Id))
                url = $"https://knaben.xyz/?id={h.Id}";
            if (string.IsNullOrWhiteSpace(url)) return null;

            var title = HttpUtility.HtmlDecode(h.Title.Trim());
            var createTime = ParseDate(h.Date) ?? ParseDate(h.LastSeen) ?? DateTime.UtcNow;
            var updateTime = ParseDate(h.LastSeen) ?? createTime;
            var (name, relased) = ParseNameAndYear(title);

            title = BuildTitleForFileDB(title);
            if (!string.IsNullOrWhiteSpace(h.Tracker) && !title.Contains(h.Tracker))
                title = $"{title} | {h.Tracker}";

            return new TorrentDetails
            {
                trackerName = TrackerName,
                types = types,
                url = url,
                title = title,
                sid = h.Seeders,
                pir = h.Peers,
                sizeName = FormatSize(h.Bytes),
                createTime = createTime,
                updateTime = updateTime,
                magnet = !string.IsNullOrWhiteSpace(h.MagnetUrl) ? h.MagnetUrl : null,
                _sn = string.IsNullOrWhiteSpace(h.MagnetUrl) && !string.IsNullOrWhiteSpace(h.Link) ? h.Link : null,
                name = name,
                originalname = name,
                relased = relased
            };
        }

        static int GetQualityFromCategoryId(int[] ids)
        {
            if (ids == null) return 480;
            foreach (var id in ids)
            {
                if (id == 2003000 || id == 3003000) return 2160;
                if (id == 2001000 || id == 3001000) return 1080;
                if (id == 2002000 || id == 3002000) return 720;
            }
            return 480;
        }

        static string[] GetTypesFromCategoryId(int[] ids)
        {
            if (ids == null || ids.Length == 0) return new[] { "movie", "serial" };
            foreach (var id in ids)
            {
                if (id >= 2000000 && id < 3000000) return new[] { "serial" };
                if (id >= 3000000 && id < 4000000) return new[] { "movie" };
            }
            return new[] { "movie", "serial" };
        }

        static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dt) ? dt : (DateTime?)null;
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1073741824L) return $"{bytes / 1048576.0:F2} Mb";
            if (bytes < 1099511627776L) return $"{bytes / 1073741824.0:F2} GB";
            return $"{bytes / 1099511627776.0:F2} TB";
        }
    }
}
