using System;
using System.Text.RegularExpressions;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Lostfilm
{
    public static partial class LostfilmParser
    {
        /// <summary>True when name and originalname differ (RU title present) — good FileDB bucket.</summary>
        public static bool HasRuName(string name, string originalname)
        {
            return !string.IsNullOrWhiteSpace(name)
                && !string.IsNullOrWhiteSpace(originalname)
                && !string.Equals(name.Trim(), originalname.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasRuName(TorrentDetails t) =>
            t != null && HasRuName(t.name, t.originalname);

        /// <summary>Slug-only bucket (Ponies:Ponies) — prefer replacing with RU/EN pair when available.</summary>
        public static bool IsXxNameBucket(string name, string originalname) =>
            !HasRuName(name, originalname);

        /// <summary>
        /// Prefer combined PlayEpisode id (6+ digits), else build from PlayEpisode('id','s','e') → id + sss + eee.
        /// Mirrors jacred-go lostfilm.getMagnet.
        /// </summary>
        public static string TryExtractPlayEpisodeId(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var longId = Regex.Match(html, @"PlayEpisode\s*\(\s*['""](\d{6,})['""]", RegexOptions.IgnoreCase);
            if (longId.Success)
                return longId.Groups[1].Value;

            var three = Regex.Match(
                html,
                @"PlayEpisode\s*\(\s*['""](\d+)['""]\s*,\s*['""](\d+)['""]\s*,\s*['""](\d+)['""]",
                RegexOptions.IgnoreCase);
            if (three.Success
                && int.TryParse(three.Groups[2].Value, out int season)
                && int.TryParse(three.Groups[3].Value, out int episode))
            {
                return $"{three.Groups[1].Value}{season:D3}{episode:D3}";
            }

            var shortId = Regex.Match(html, @"PlayEpisode\s*\(\s*['""]?(\d+)['""]?\s*\)", RegexOptions.IgnoreCase);
            return shortId.Success ? shortId.Groups[1].Value : null;
        }

        /// <summary>PlayMovie / PlayEpisode id from movie page (Go getVURLFromMoviePage).</summary>
        public static string TryExtractPlayMovieOrEpisodeId(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var longId = Regex.Match(html, @"Play(?:Movie|Episode)\s*\(\s*['""](\d{6,})['""]", RegexOptions.IgnoreCase);
            if (longId.Success)
                return longId.Groups[1].Value;

            var three = Regex.Match(
                html,
                @"Play(?:Movie|Episode)\s*\(\s*['""](\d+)['""]\s*,\s*['""](\d+)['""]\s*,\s*['""](\d+)['""]",
                RegexOptions.IgnoreCase);
            if (three.Success
                && int.TryParse(three.Groups[2].Value, out int season)
                && int.TryParse(three.Groups[3].Value, out int episode))
            {
                return $"{three.Groups[1].Value}{season:D3}{episode:D3}";
            }

            var shortId = Regex.Match(html, @"Play(?:Movie|Episode)\s*\(\s*['""]?(\d+)['""]?\s*\)", RegexOptions.IgnoreCase);
            return shortId.Success ? shortId.Groups[1].Value : null;
        }

        public static string StripUrlFragment(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;
            int hash = url.IndexOf('#');
            return hash >= 0 ? url.Substring(0, hash) : url;
        }

        public static bool IsEpisodePathUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            string path = StripUrlFragment(url);
            return Regex.IsMatch(path, @"/series/[^/]+/season_\d+/episode_\d+/?", RegexOptions.IgnoreCase);
        }

        public static string AppendQualityToTitle(string title, string quality)
        {
            string q = NormalizeQuality(quality);
            if (string.IsNullOrEmpty(q))
                return title ?? "";
            string t = (title ?? "").TrimEnd();
            if (t.EndsWith("]", StringComparison.Ordinal))
                return t.Substring(0, t.Length - 1) + ", " + q + "]";
            return t + " [" + q + "]";
        }

        /// <summary>Apply cached magnet/size; restore name only when cache has better RU pair and incoming is X:X.</summary>
        public static void ApplyMagnetCache(TorrentDetails incoming, TorrentDetails cached)
        {
            if (incoming == null || cached == null)
                return;
            if (!string.IsNullOrEmpty(cached.magnet))
                incoming.magnet = cached.magnet;
            if (!string.IsNullOrEmpty(cached.sizeName))
                incoming.sizeName = cached.sizeName;
            // Do not restore title from cache — keep incoming title/quality.
            if (IsXxNameBucket(incoming.name, incoming.originalname) && HasRuName(cached))
            {
                incoming.name = cached.name;
                incoming.originalname = cached.originalname;
            }
        }

        /// <summary>Clone episode row for one quality with #suffix URL (movies/season-pack scheme).</summary>
        public static TorrentDetails CloneWithQuality(TorrentDetails source, string magnet, string quality, string sizeName)
        {
            string q = NormalizeQuality(quality);
            string bare = StripUrlFragment(source.url);
            return new TorrentDetails
            {
                trackerName = source.trackerName,
                types = source.types,
                url = bare + "#" + q,
                title = AppendQualityToTitle(source.title, q),
                sid = source.sid,
                pir = source.pir,
                createTime = source.createTime,
                name = source.name,
                originalname = source.originalname,
                relased = source.relased,
                magnet = magnet,
                sizeName = sizeName ?? ""
            };
        }

        /// <summary>Canonical path+fragment for stable FileDB id (host-independent).</summary>
        public static string CanonicalLostfilmPath(string url)
        {
            if (string.IsNullOrEmpty(url))
                return "";
            string u = url.Trim();
            var m = Regex.Match(u, @"https?://[^/]+(/.*)$", RegexOptions.IgnoreCase);
            string path = m.Success ? m.Groups[1].Value : u;
            // Normalize episode/movie paths; keep fragment for quality distinction.
            path = path.Replace('\\', '/');
            return path.ToLowerInvariant();
        }

        /// <summary>Stable positive hash for GetTorrentIdFromUrl (includes #quality so qualities stay distinct).</summary>
        public static int StableUrlId(string url)
        {
            string key = CanonicalLostfilmPath(url);
            if (string.IsNullOrEmpty(key))
                return 0;
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < key.Length; i++)
                {
                    hash ^= key[i];
                    hash *= 16777619;
                }
                int id = (int)(hash & 0x7FFFFFFF);
                return id == 0 ? 1 : id;
            }
        }
    }
}
