using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Models.Details;
using JacRed.Models.tParse;

namespace JacRed.Infrastructure.Trackers.Aniliberty
{
    public static class AnilibertyParser
    {
        const string TrackerName = "aniliberty";

        public static List<TorrentDetails> MapPageTorrents(AnilibertyApiResponse response, string host)
        {
            if (response?.Data == null || response.Data.Count == 0)
                return new List<TorrentDetails>();

            var torrents = new List<TorrentDetails>();

            foreach (var apiTorrent in response.Data)
            {
                var mapped = MapApiTorrent(apiTorrent, host);
                if (mapped != null)
                    torrents.Add(mapped);
            }

            return torrents;
        }

        public static TorrentDetails MapApiTorrent(AnilibertyTorrent apiTorrent, string host)
        {
            if (string.IsNullOrWhiteSpace(apiTorrent.Magnet) || apiTorrent.Release == null)
                return null;

            var release = apiTorrent.Release;
            string name = release.Name?.Main?.Trim();
            string originalname = release.Name?.English?.Trim();

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(originalname))
                return null;

            string qualityInfo = ExtractQualityInfo(apiTorrent.Label);

            string baseTitle;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(originalname) && !string.Equals(name, originalname, StringComparison.Ordinal))
                baseTitle = $"{name} / {originalname}";
            else if (!string.IsNullOrWhiteSpace(name))
                baseTitle = name;
            else if (!string.IsNullOrWhiteSpace(originalname))
                baseTitle = originalname;
            else
                baseTitle = "Unknown";

            string title = baseTitle;

            if (release.Year.HasValue)
                title += $" / {release.Year.Value}";

            if (!string.IsNullOrWhiteSpace(qualityInfo))
                title += $" / {qualityInfo}";

            string[] types = DetermineTypes(release.Type?.Value);

            DateTime createTime = default;
            if (!string.IsNullOrWhiteSpace(apiTorrent.CreatedAt) &&
                DateTime.TryParse(apiTorrent.CreatedAt, out DateTime parsedDate))
                createTime = parsedDate.ToUniversalTime();

            if (createTime == default)
                createTime = DateTime.UtcNow;

            DateTime updateTime = default;
            if (!string.IsNullOrWhiteSpace(apiTorrent.UpdatedAt) &&
                DateTime.TryParse(apiTorrent.UpdatedAt, out parsedDate))
                updateTime = parsedDate.ToUniversalTime();

            if (updateTime == default)
                updateTime = createTime;

            string baseUrl = !string.IsNullOrWhiteSpace(release.Alias)
                ? $"{host}/anime/releases/release/{release.Alias}"
                : $"{host}/api/v1/anime/torrents/{apiTorrent.Hash}";

            string torrentUrl = $"{baseUrl}?hash={apiTorrent.Hash}";
            string sizeName = FormatSize(apiTorrent.Size);
            int quality = ParseQuality(apiTorrent.Quality?.Value);
            string videotype = apiTorrent.Type?.Value?.ToLowerInvariant();

            return new TorrentDetails
            {
                trackerName = TrackerName,
                types = types,
                url = torrentUrl,
                title = title,
                sid = apiTorrent.Seeders,
                pir = apiTorrent.Leechers,
                createTime = createTime,
                updateTime = updateTime,
                name = name,
                originalname = originalname,
                relased = release.Year ?? 0,
                magnet = apiTorrent.Magnet,
                sizeName = sizeName,
                quality = quality,
                videotype = videotype
            };
        }

        public static string[] DetermineTypes(string typeValue)
        {
            if (string.IsNullOrWhiteSpace(typeValue))
                return new[] { "anime" };

            string typeUpper = typeValue.ToUpperInvariant();

            switch (typeUpper)
            {
                case "MOVIE":
                    return new[] { "anime", "movie" };
                case "OVA":
                case "OAD":
                    return new[] { "anime", "ova" };
                case "SPECIAL":
                    return new[] { "anime", "special" };
                case "ONA":
                case "WEB":
                    return new[] { "anime", "ona" };
                case "DORAMA":
                    return new[] { "dorama" };
                case "TV":
                default:
                    return new[] { "anime", "serial" };
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1073741824L)
                return $"{bytes / 1048576.0:F2} Mb";
            if (bytes < 1099511627776L)
                return $"{bytes / 1073741824.0:F2} GB";
            return $"{bytes / 1099511627776.0:F2} TB";
        }

        public static int ParseQuality(string qualityValue)
        {
            if (string.IsNullOrWhiteSpace(qualityValue))
                return 480;

            string q = qualityValue.ToLowerInvariant().Trim();

            if (q.Contains("4k") || q.Contains("2160p") || q.Contains("uhd"))
                return 2160;

            var match = Regex.Match(q, @"(\d{3,4})p?");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int quality))
            {
                if (quality >= 2160)
                    return 2160;
                if (quality >= 1080)
                    return 1080;
                if (quality >= 720)
                    return 720;
                if (quality >= 480)
                    return 480;
                return quality;
            }

            return 480;
        }

        public static string ExtractQualityInfo(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return string.Empty;

            var match = Regex.Match(label, @"(\[[^\]]+\](?:\s*\[[^\]]+\])*)\s*$");
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }
    }
}
