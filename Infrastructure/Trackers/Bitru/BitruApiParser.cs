using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.Bitru
{
    public static class BitruApiParser
    {
        /// <summary>
        /// Убирает из названия сезон, эпизод, качество и т.д. — для name/originalname.
        /// API v2 ищет по базовому имени; сезон указывается отдельным параметром season.
        /// Публичный для использования в FixBitruNamesMigration.
        /// </summary>
        public static string CleanTitleForSearch(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;
            string t = title.Trim();

            var yearMatch = Regex.Match(t, @"[\(\[](\d{4})[\)\]]");
            if (yearMatch.Success && yearMatch.Index > 0)
                t = t.Substring(0, yearMatch.Index);

            t = Regex.Replace(t, @"\b(S\d{1,2}E\d{1,2}|S\d{1,2}E?\d{0,2}|E\d{1,2}|\d{1,2}x\d{1,2})\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s*\d{1,2}(-\d{1,2})?\s*сезон\s*.*$", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(Сезон|Season)\s*\d{1,2}(?!\d).*$", "", RegexOptions.IgnoreCase);

            t = Regex.Replace(t, @"\b(2160p|1080p|720p|480p)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(WEB[-\s]?DL|WEB[-\s]?Rip|BDRip|BDRemux|HDRip|BluRay|BRRip|DVDRip|HDTV)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(x264|x265|h\.?264|h\.?265|hevc|avc|aac|ac3|dts)\b", "", RegexOptions.IgnoreCase);

            t = Regex.Replace(t, @"[\[\]\|]", " ");
            t = Regex.Replace(t, @"\s{2,}", " ").Trim().TrimEnd(' ', '/', '-', '|');
            t = Regex.Replace(t, @"[.\s]+-\s*[A-Za-z0-9][A-Za-z0-9.-]*$", "", RegexOptions.IgnoreCase);
            return t.Trim().TrimEnd(' ', '-');
        }

        public static List<TorrentDetails> ParseTorrentsFromJson(string json, string hostUrl)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<TorrentDetails>();

            BitruApiResponse response;
            try
            {
                response = JsonConvert.DeserializeObject<BitruApiResponse>(json);
            }
            catch
            {
                return new List<TorrentDetails>();
            }

            return ParseTorrentsFromResponse(response, hostUrl);
        }

        public static List<TorrentDetails> ParseTorrentsFromResponse(BitruApiResponse response, string hostUrl)
        {
            var torrents = new List<TorrentDetails>();
            if (response == null || response.HasError || response.Result?.Items == null)
                return torrents;

            foreach (var wrap in response.Result.Items)
            {
                if (wrap?.Item == null)
                    continue;

                var t = MapToTorrentDetails(wrap.Item, hostUrl);
                if (t != null)
                    torrents.Add(t);
            }

            return torrents;
        }

        public static TorrentDetails MapToTorrentDetails(BitruApiItemInner item, string hostUrl)
        {
            var torrent = item.Torrent;
            var info = item.Info;
            var template = item.Template;

            if (torrent == null || info == null || template == null)
                return null;

            if (!BitruCategories.TryGetTypes(template.Category, template.Subsection, out string[] types))
                return null;

            string name = CleanTitleForSearch(info.Name ?? "")?.Trim();
            string originalname = CleanTitleForSearch(template.OrigName ?? "")?.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = (info.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(originalname)) originalname = (template.OrigName ?? "").Trim();
            string yearDisplay = BitruYearToDisplayString(info.Year);
            int relased = BitruYearToReleased(info.Year);

            string nameRaw = (info.Name ?? "").Trim();
            string originalnameRaw = (template.OrigName ?? "").Trim();
            string titlePart = nameRaw;
            if (!string.IsNullOrWhiteSpace(originalnameRaw))
                titlePart += " / " + originalnameRaw;
            if (!string.IsNullOrEmpty(yearDisplay))
                titlePart += " (" + yearDisplay + ")";
            if (template.Video?.Quality != null)
                titlePart += " " + template.Video.Quality;
            if (!string.IsNullOrWhiteSpace(template.Other))
                titlePart += " | " + template.Other;

            hostUrl = (hostUrl ?? "").TrimEnd('/');
            string url = $"{hostUrl}/details.php?id={torrent.Id}";
            string sizeName = FormatSize(torrent.Size);
            DateTime createTime = DateTimeOffset.FromUnixTimeSeconds(torrent.Added).UtcDateTime;

            string downloadUrl = torrent.File;
            if (string.IsNullOrWhiteSpace(downloadUrl) || !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                downloadUrl = $"{hostUrl}/api.php?download={torrent.Id}";

            return new TorrentDetails
            {
                trackerName = "bitru",
                types = types,
                url = url,
                title = HttpUtility.HtmlDecode(titlePart.Trim()),
                sid = torrent.Seeders,
                pir = torrent.Leechers,
                sizeName = sizeName,
                createTime = createTime,
                name = name.Trim(),
                originalname = originalname?.Trim(),
                relased = relased,
                _sn = downloadUrl
            };
        }

        /// <summary>Unix timestamp for ParseFromDate after_date filter (start of calendar day, UTC offset 0).</summary>
        public static long UnixFromDate(DateTime fromDate)
        {
            return new DateTimeOffset(fromDate.Year, fromDate.Month, fromDate.Day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        }

        static string BitruYearToDisplayString(object year)
        {
            if (year == null) return "";
            if (year is long l) return l.ToString();
            if (year is int i) return i.ToString();
            return year.ToString()?.Trim() ?? "";
        }

        static int BitruYearToReleased(object year)
        {
            if (year == null) return 0;
            if (year is long l) return (int)l;
            if (year is int i) return i;
            var s = year.ToString()?.Trim();
            if (string.IsNullOrEmpty(s)) return 0;
            var dash = s.IndexOf('-');
            var firstPart = dash > 0 ? s.Substring(0, dash).Trim() : s;
            return int.TryParse(firstPart, NumberStyles.None, CultureInfo.InvariantCulture, out int y) ? y : 0;
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1000L * 1024)
                return $"{bytes / 1024.0:F2} КБ";
            if (bytes < 1000L * 1048576)
                return $"{bytes / 1048576.0:F2} МБ";
            if (bytes < 1000L * 1073741824)
                return $"{bytes / 1073741824.0:F2} ГБ";
            return $"{bytes / 1099511627776.0:F2} ТБ";
        }
    }
}
