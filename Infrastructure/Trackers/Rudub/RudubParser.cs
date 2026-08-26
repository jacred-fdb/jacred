using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Rudub
{
    /// <summary>
    /// RuDub (ex-BaibaKoTV) listing parser — card layout on browse.php.
    /// Keeps only HD 1080 / HD 2160 titles (drops XviD, x264-SD, HD 720).
    /// </summary>
    public static class RudubParser
    {
        public const string TrackerName = "rudub";
        public const string ValidationMarker = "card__torlist__browse_2";
        public const string EndpointDownload = "/download2.php";

        /// <summary>Site videoformat query: 4 = HD 1080, 5 = HD 2160.</summary>
        public static readonly int[] PreferredVideoFormats = { 4, 5 };

        const string TypeSerial = "serial";
        const string TypeMovie = "movie";

        static readonly Regex RegexCardSplit = new Regex(
            @"<div\s+class=""card__torlist__browse_2""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RegexDetails = new Regex(
            @"href=[""']/?(details\.php\?id=([0-9]+))[""'][^>]*>\s*<b>([\s\S]*?)</b>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RegexDownloadId = new Regex(
            @"href=[""']/?(?:download2\.php\?id=|download\.php\?id=)([0-9]+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RegexDate = new Regex(
            @"li\s+title=[""']Дата[""'][^>]*>[\s\S]*?</i>\s*([0-9]{4}-[0-9]{2}-[0-9]{2}\s+[0-9]{2}:[0-9]{2}:[0-9]{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RegexSize = new Regex(
            @"li\s+title=[""']Размер[""'][^>]*>[\s\S]*?</i>\s*([^<]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RegexActivity = new Regex(
            @"li\s+title=[""']Активность[""'][^>]*>[\s\S]*?</i>\s*(\d+)\s*<[\s\S]*?</i>\s*(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Accept HD1080p / HD2160p (and BD/HDR variants); reject 720 and SD.</summary>
        static readonly Regex RegexGoodQuality = new Regex(
            @"(?i)(?:\b|[^0-9])(?:HD|BD|HDR)?(?:1080p|2160p)\b",
            RegexOptions.Compiled);

        static readonly Regex RegexBadQuality = new Regex(
            @"(?i)(?:\bWEBRip\s*XviD\b|\bWEBRip\s*x264\b|\bHD720p\b|(?<![0-9])720p\b)",
            RegexOptions.Compiled);

        static readonly Regex RegexNameOriginal = new Regex(
            @"^\s*([^(\n]+?)\s*\(([^)]+)\)\s*",
            RegexOptions.Compiled);

        static readonly Regex RegexSerialPattern1 = new Regex(@"[CcСс]езон", RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern2 = new Regex(@"[CcСс]ери", RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern3 = new Regex(@"/\s*s\d+e\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RegexWhitespace = new Regex(@"[\n\r\t ]+", RegexOptions.Compiled);
        static readonly Regex RegexBr = new Regex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsPreferredQualityTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;
            if (RegexBadQuality.IsMatch(title) && !RegexGoodQuality.IsMatch(title))
                return false;
            return RegexGoodQuality.IsMatch(title);
        }

        public static List<RudubDetails> ParseTorrentListFromHtml(string html, string host)
        {
            var torrents = new List<RudubDetails>();
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(host))
                return torrents;

            host = host.TrimEnd('/');
            string decoded = tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", " ")));

            string[] parts = RegexCardSplit.Split(decoded);
            for (int i = 1; i < parts.Length; i++)
            {
                string card = parts[i];
                var details = RegexDetails.Match(card);
                if (!details.Success)
                    continue;

                string relUrl = details.Groups[1].Value;
                string id = details.Groups[2].Value;
                string rawTitle = details.Groups[3].Value;
                string title = NormalizeTitle(rawTitle);
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(id))
                    continue;

                if (!IsPreferredQualityTitle(title))
                    continue;

                var dl = RegexDownloadId.Match(card);
                if (!dl.Success)
                    continue;

                string downloadId = dl.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(downloadId))
                    continue;

                ParseNames(title, out string name, out string originalname);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                DateTime createTime = ParseCardDate(card);
                if (createTime == default)
                    createTime = DateTime.UtcNow;

                int sid = 1, pir = 0;
                var act = RegexActivity.Match(card);
                if (act.Success)
                {
                    int.TryParse(act.Groups[1].Value, out sid);
                    int.TryParse(act.Groups[2].Value, out pir);
                }

                string sizeName = null;
                var sizeMatch = RegexSize.Match(card);
                if (sizeMatch.Success)
                    sizeName = RegexWhitespace.Replace(sizeMatch.Groups[1].Value, " ").Trim();

                int quality = DetectQuality(title);

                torrents.Add(new RudubDetails
                {
                    trackerName = TrackerName,
                    types = DetectContentType(title),
                    url = $"{host}/{relUrl}",
                    title = title,
                    name = name,
                    originalname = originalname,
                    sid = sid,
                    pir = pir,
                    createTime = createTime,
                    sizeName = sizeName,
                    quality = quality,
                    downloadUri = $"{host}{EndpointDownload}?id={downloadId}"
                });
            }

            return torrents;
        }

        public static bool TypesEqual(string[] types1, string[] types2)
        {
            if (types1 == null && types2 == null) return true;
            if (types1 == null || types2 == null) return false;
            return types1.SequenceEqual(types2);
        }

        public static bool IsValidBencodedTorrent(byte[] data)
        {
            if (data == null || data.Length == 0)
                return false;

            if (data[0] == (byte)'d')
                return true;

            if (data.Length < 100)
            {
                string preview = Encoding.UTF8.GetString(data, 0, Math.Min(200, data.Length));
                if (preview.Contains("<html") || preview.Contains("<!DOCTYPE") || preview.Contains("<body"))
                    return false;
            }

            return false;
        }

        static string NormalizeTitle(string raw)
        {
            string t = RegexBr.Replace(raw ?? "", " ");
            t = RegexWhitespace.Replace(HttpUtility.HtmlDecode(t), " ").Trim();
            t = t.Replace("(Обновляемая)", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("(Оновлюється)", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("(Золото)", "", StringComparison.OrdinalIgnoreCase);
            return RegexWhitespace.Replace(t, " ").Trim();
        }

        static void ParseNames(string title, out string name, out string originalname)
        {
            name = null;
            originalname = null;
            var m = RegexNameOriginal.Match(title);
            if (m.Success)
            {
                name = m.Groups[1].Value.Trim();
                originalname = m.Groups[2].Value.Trim();
                return;
            }

            name = Regex.Split(title, @"(\(|/|\|)", RegexOptions.IgnoreCase)[0].Trim();
        }

        static DateTime ParseCardDate(string card)
        {
            var m = RegexDate.Match(card);
            if (!m.Success)
                return default;

            if (DateTime.TryParseExact(
                    m.Groups[1].Value.Trim(),
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dt))
                return dt;

            return default;
        }

        static int DetectQuality(string title)
        {
            if (Regex.IsMatch(title, @"(?i)2160p"))
                return 2160;
            if (Regex.IsMatch(title, @"(?i)1080p"))
                return 1080;
            return 0;
        }

        static string[] DetectContentType(string title)
        {
            bool isSerial = RegexSerialPattern1.IsMatch(title) ||
                            RegexSerialPattern2.IsMatch(title) ||
                            RegexSerialPattern3.IsMatch(title);
            return isSerial
                ? new[] { TypeSerial }
                : new[] { TypeMovie };
        }
    }
}
