using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Engine.Parsing;
using JacRed.Models.Details;

namespace JacRed.Engine.Trackers.Baibako
{
    public static class BaibakoParser
    {
        const string TrackerName = "baibako";
        const string TypeSerial = "serial";
        const string TypeMovie = "movie";
        const string EndpointDownload = "/download.php";

        static readonly Regex RegexSerialPattern1 = new Regex("/s\\d+e\\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern2 = new Regex("\\d+[\\-й]?\\s*сезон", RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern3 = new Regex("сезон\\s+повністю", RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern4 = new Regex("сезон\\s+полностью", RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern5 = new Regex("полный\\s+\\d+\\s+сезон", RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern6 = new Regex("повній\\s+\\d+[\\-й]?\\s*сезон", RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern7 = new Regex("\\d+[\\-й]?\\s*сезон\\s+повністю", RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern8 = new Regex("\\d+[\\-й]?\\s*сезон\\s+полностью", RegexOptions.Compiled);
        static readonly Regex RegexSerialPattern9 = new Regex("сезон\\s+\\d+", RegexOptions.Compiled);
        static readonly Regex RegexDownloadId = new Regex("href=[\"']/?(?:download\\.php\\?id=|download\\.php&amp;id=)([0-9]+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RegexWhitespace = new Regex("[\\n\\r\\t ]+", RegexOptions.Compiled);
        static readonly Regex RegexTitleFormat = new Regex("([^/\\(]+)[^/]+/([^/\\(]+)", RegexOptions.Compiled);
        static readonly Regex RegexQualityFilter = new Regex("(1080p|720p)", RegexOptions.Compiled);

        public const string ValidationNavTop = "id=\"navtop\"";

        public static List<BaibakoDetails> ParseTorrentListFromHtml(string html, string host, int page)
        {
            var torrents = new List<BaibakoDetails>();

            foreach (string row in tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", ""))).Split("<tr").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;

                DateTime createTime = tParse.ParseCreateTime(ExtractAndClean(row, "<small>(?:Загружена|Обновлена): ([0-9]+ [^ ]+ [0-9]{4}) в [^<]+</small>"), "dd.MM.yyyy");
                if (createTime == default)
                {
                    if (page != 0)
                        continue;

                    createTime = DateTime.UtcNow;
                }

                var gurl = Regex.Match(row, "<a href=\"/?(details.php\\?id=[0-9]+)[^\"]+\">([^<]+)</a>").Groups;

                string url = gurl[1].Value;
                string title = gurl[2].Value;

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
                    continue;

                title = title.Replace("(Обновляемая)", "").Replace("(Золото)", "").Replace("(Оновлюється)", "");
                title = Regex.Replace(title, "/( +| )?$", "").Trim();

                if (!RegexQualityFilter.IsMatch(title))
                    continue;

                url = $"{host}/{url}";

                string name = null, originalname = null;

                var g = RegexTitleFormat.Match(title).Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[1].Value.Trim();
                    originalname = g[2].Value.Trim();
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    var downloadMatch = RegexDownloadId.Match(row);
                    if (!downloadMatch.Success)
                        continue;

                    string downloadId = downloadMatch.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(downloadId))
                        continue;

                    string[] types = DetectContentType(title);

                    torrents.Add(new BaibakoDetails()
                    {
                        trackerName = TrackerName,
                        types = types,
                        url = url,
                        title = title,
                        sid = 1,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        downloadUri = $"{host}{EndpointDownload}?id={downloadId}"
                    });
                }
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

        static string ExtractAndClean(string text, string pattern)
        {
            string res = new Regex(pattern, RegexOptions.IgnoreCase).Match(text).Groups[1].Value.Trim();
            res = RegexWhitespace.Replace(res, " ");
            return res.Trim();
        }

        static string[] DetectContentType(string title)
        {
            string titleLower = title.ToLower();

            bool isSerial = RegexSerialPattern1.IsMatch(title) ||
                           RegexSerialPattern2.IsMatch(titleLower) ||
                           RegexSerialPattern3.IsMatch(titleLower) ||
                           RegexSerialPattern4.IsMatch(titleLower) ||
                           RegexSerialPattern5.IsMatch(titleLower) ||
                           RegexSerialPattern6.IsMatch(titleLower) ||
                           RegexSerialPattern7.IsMatch(titleLower) ||
                           RegexSerialPattern8.IsMatch(titleLower) ||
                           RegexSerialPattern9.IsMatch(titleLower);

            return isSerial
                ? new string[] { TypeSerial }
                : new string[] { TypeMovie };
        }
    }
}
