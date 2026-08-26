using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Models.Details;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JacRed.Infrastructure.Trackers.SubsPlease
{
    /// <summary>
    /// SubsPlease JSON API parser — keeps only 1080p magnets; maps full release metadata.
    /// </summary>
    public static class SubsPleaseParser
    {
        public const string TrackerName = "subsplease";
        public const string PreferredRes = "1080";

        static readonly Regex RegexXl = new Regex(@"[?&]xl=(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RegexBtih = new Regex(
            @"xt=urn:btih:([A-Za-z0-9]{32,40})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RegexShowSid = new Regex(
            @"id=[""']show-release-table[""'][^>]*\bsid=[""'](\d+)[""']|\bsid=[""'](\d+)[""'][^>]*id=[""']show-release-table[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RegexShowSidLoose = new Regex(
            @"<table[^>]*id=[""']show-release-table[""'][^>]*\bsid=[""'](\d+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RegexShowLink = new Regex(
            @"href=[""']/shows/([^""'/]+)/?[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsBatchEpisode(string episode)
        {
            if (string.IsNullOrWhiteSpace(episode))
                return false;
            string ep = episode.Trim();
            if (ep.IndexOf('-') >= 0)
                return true;
            return Regex.IsMatch(ep, @"^\d+\s*~\s*\d+$");
        }

        public static bool IsLimitReached(string json)
        {
            return !string.IsNullOrWhiteSpace(json) &&
                   json.IndexOf("limit_reached", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<SubsPleaseDetails> ParseLatestOrSearchJson(string json, string host)
        {
            var list = new List<SubsPleaseDetails>();
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]" || IsLimitReached(json))
                return list;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonReaderException)
            {
                return list;
            }

            foreach (var prop in root.Properties())
            {
                if (prop.Value is not JObject obj)
                    continue;
                var release = obj.ToObject<SubsPleaseReleaseDto>();
                if (release == null)
                    continue;
                list.AddRange(BuildFromRelease(release, host, showSid: null, section: null));
            }

            return list;
        }

        public static List<SubsPleaseDetails> ParseShowJson(string json, string host, string pageSlug, string showSid)
        {
            var list = new List<SubsPleaseDetails>();
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
                return list;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonReaderException)
            {
                return list;
            }

            foreach (string section in new[] { "batch", "episode" })
            {
                if (root[section] is not JObject bag)
                    continue;
                foreach (var prop in bag.Properties())
                {
                    if (prop.Value is not JObject obj)
                        continue;
                    var release = obj.ToObject<SubsPleaseReleaseDto>();
                    if (release == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(release.Page))
                        release.Page = pageSlug;
                    list.AddRange(BuildFromRelease(release, host, showSid, section));
                }
            }

            return list;
        }

        public static List<string> ParseShowSlugsFromIndexHtml(string html)
        {
            var slugs = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
                return slugs;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in RegexShowLink.Matches(html))
            {
                string slug = m.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(slug) || slug.Equals("shows", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(slug))
                    slugs.Add(slug);
            }

            return slugs;
        }

        public static string ExtractShowSidFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var m = RegexShowSidLoose.Match(html);
            if (m.Success)
                return m.Groups[1].Value;

            m = RegexShowSid.Match(html);
            if (m.Success)
                return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;

            return null;
        }

        public static List<string> ParseSchedulePageSlugs(string json)
        {
            var slugs = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
                return slugs;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonReaderException)
            {
                return slugs;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root["schedule"] is not JObject days)
                return slugs;

            foreach (var day in days.Properties())
            {
                if (day.Value is not JArray arr)
                    continue;
                foreach (var item in arr.OfType<JObject>())
                {
                    string page = item.Value<string>("page");
                    if (string.IsNullOrWhiteSpace(page))
                        continue;
                    if (seen.Add(page.Trim()))
                        slugs.Add(page.Trim());
                }
            }

            return slugs;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0)
                return null;
            if (bytes < 1073741824L)
                return $"{bytes / 1048576.0:F2} Mb";
            if (bytes < 1099511627776L)
                return $"{bytes / 1073741824.0:F2} GB";
            return $"{bytes / 1099511627776.0:F2} TB";
        }

        public static long? TryParseXl(string magnet)
        {
            if (string.IsNullOrWhiteSpace(magnet))
                return null;
            var m = RegexXl.Match(magnet);
            if (!m.Success || !long.TryParse(m.Groups[1].Value, out long xl) || xl <= 0)
                return null;
            return xl;
        }

        public static string TryParseInfoHash(string magnet)
        {
            if (string.IsNullOrWhiteSpace(magnet))
                return null;
            var m = RegexBtih.Match(magnet);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        public static string BuildUrl(string host, string page, string episode)
        {
            host = (host ?? "").TrimEnd('/');
            page = (page ?? "").Trim().Trim('/');
            string ep = Uri.EscapeDataString(episode ?? "");
            return $"{host}/shows/{page}/?ep={ep}&res={PreferredRes}";
        }

        /// <summary>Stable positive int from episode + res for FileDB path id.</summary>
        public static int StableUrlId(string episode)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string key = $"{episode ?? ""}|{PreferredRes}";
                foreach (char c in key)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                int id = (int)(hash & 0x7FFFFFFF);
                return id == 0 ? 1 : id;
            }
        }

        public static string BuildTitle(string show, string episode, bool isBatch)
        {
            string ep = string.IsNullOrWhiteSpace(episode) ? "?" : episode.Trim();
            string title = $"[SubsPlease] {show?.Trim()} - {ep} ({PreferredRes}p)";
            if (isBatch)
                title += " [Batch]";
            return title;
        }

        static IEnumerable<SubsPleaseDetails> BuildFromRelease(
            SubsPleaseReleaseDto release,
            string host,
            string showSid,
            string section)
        {
            if (release?.Downloads == null || release.Downloads.Count == 0)
                yield break;

            string show = release.Show?.Trim();
            string episode = release.Episode?.Trim();
            string page = release.Page?.Trim();
            if (string.IsNullOrWhiteSpace(show) || string.IsNullOrWhiteSpace(episode) || string.IsNullOrWhiteSpace(page))
                yield break;

            bool isBatch = string.Equals(section, "batch", StringComparison.OrdinalIgnoreCase) ||
                           IsBatchEpisode(episode);

            var dl = release.Downloads.FirstOrDefault(d =>
                string.Equals(d?.Res, PreferredRes, StringComparison.OrdinalIgnoreCase));
            if (dl == null || string.IsNullOrWhiteSpace(dl.Magnet))
                yield break;

            string magnet = dl.Magnet.Trim();
            long? xl = TryParseXl(magnet);
            string infoHash = TryParseInfoHash(magnet);
            DateTime createTime = ParseReleaseDate(release.ReleaseDate) ?? DateTime.UtcNow;

            yield return new SubsPleaseDetails
            {
                trackerName = TrackerName,
                types = new[] { "anime" },
                url = BuildUrl(host, page, episode),
                title = BuildTitle(show, episode, isBatch),
                name = show,
                originalname = show,
                sid = 1,
                pir = 0,
                quality = 1080,
                sizeName = xl.HasValue ? FormatSize(xl.Value) : null,
                createTime = createTime,
                updateTime = DateTime.UtcNow,
                magnet = magnet,
                _sn = string.IsNullOrWhiteSpace(dl.Torrent) ? null : dl.Torrent.Trim(),
                showSid = showSid,
                page = page,
                episode = episode,
                isBatch = isBatch,
                infoHash = infoHash,
                imageUrl = release.ImageUrl,
                xdcc = release.Xdcc
            };
        }

        static DateTime? ParseReleaseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return null;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return dto.UtcDateTime;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;
            return null;
        }

        public class SubsPleaseReleaseDto
        {
            [JsonProperty("show")]
            public string Show { get; set; }

            [JsonProperty("episode")]
            public string Episode { get; set; }

            [JsonProperty("page")]
            public string Page { get; set; }

            [JsonProperty("release_date")]
            public string ReleaseDate { get; set; }

            [JsonProperty("time")]
            public string Time { get; set; }

            [JsonProperty("image_url")]
            public string ImageUrl { get; set; }

            [JsonProperty("xdcc")]
            public string Xdcc { get; set; }

            [JsonProperty("downloads")]
            public List<SubsPleaseDownloadDto> Downloads { get; set; }
        }

        public class SubsPleaseDownloadDto
        {
            [JsonProperty("res")]
            public string Res { get; set; }

            [JsonProperty("magnet")]
            public string Magnet { get; set; }

            [JsonProperty("torrent")]
            public string Torrent { get; set; }
        }
    }
}
