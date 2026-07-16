using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Trackers.Lostfilm
{
    public static partial class LostfilmParser
    {
        /// <summary>Парсит HTML страницы InSearch (V/?c=...) и извлекает только 1080p / 2160p torrent-ссылки (без скачивания).</summary>
        public static List<(string torrentUrl, string quality)> ParseVPageQualityLinkUrls(string searchHtml)
        {
            if (string.IsNullOrEmpty(searchHtml) || !searchHtml.Contains("inner-box--link"))
                return new List<(string, string)>();

            string flat = Regex.Replace(searchHtml, @"[\n\r\t]+", " ");
            var linkRe = new Regex(@"<div\s+class=""inner-box--link\s+main""[^>]*><a\s+href=""([^""]+)""[^>]*>([^<]+)</a></div>", RegexOptions.IgnoreCase);
            var results = new List<(string torrentUrl, string quality)>();

            foreach (Match m in linkRe.Matches(flat))
            {
                string linkText = m.Groups[2].Value;
                string quality = Regex.Match(linkText, @"(2160p|1080p)", RegexOptions.IgnoreCase).Groups[1].Value;
                if (string.IsNullOrEmpty(quality))
                    quality = Regex.Match(linkText, @"\b(2160|1080)\b", RegexOptions.IgnoreCase).Groups[1].Value;
                if (string.IsNullOrEmpty(quality))
                    continue;
                quality = NormalizeQuality(quality);
                if (!IsPreferredQuality(quality))
                    continue;
                string torrentUrl = m.Groups[1].Value;
                if (string.IsNullOrEmpty(torrentUrl))
                    continue;
                results.Add((torrentUrl, quality));
            }
            return results;
        }
    }
}
