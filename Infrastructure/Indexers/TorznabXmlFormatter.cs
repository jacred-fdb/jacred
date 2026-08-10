using JacRed.Models.Api;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Indexers
{
    public static class TorznabXmlFormatter
    {
        static readonly Regex Cyrillic = new Regex(@"[а-яА-ЯёЁ]");
        static readonly Regex Latin = new Regex(@"[a-zA-Z]");
        static readonly Regex InfoHashFromMagnet = new Regex(@"btih:([a-fA-F0-9]+)", RegexOptions.IgnoreCase);

        public static string CapsXml(string apiUrl)
        {
            var apiEsc = EscapeXml(apiUrl.TrimEnd('/'));
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
  <server version=""1.0"" title=""JacRed"" strapline=""Native Torznab API"" email=""info@localhost"" url=""{apiEsc}""/>
  <limits max=""1000"" default=""100""/>
  <searching>
    <search available=""yes"" supportedParams=""q,imdbid""/>
    <tv-search available=""yes"" supportedParams=""q,imdbid,tvdbid,season,ep""/>
    <movie-search available=""yes"" supportedParams=""q,imdbid""/>
  </searching>
  <categories>
    <category id=""2000"" name=""Movies""/>
    <category id=""5000"" name=""TV""/>
    <category id=""5070"" name=""TV/Anime""/>
  </categories>
</caps>";
        }

        public static string IndexersXml(IEnumerable<string> trackers)
        {
            var sb = new StringBuilder(@"<?xml version=""1.0"" encoding=""UTF-8""?>
<indexers>
  <indexer id=""all"" configured=""true"">
    <title>JacRed (all trackers)</title>
    <description>Aggregated JacRed search across all configured trackers</description>
    <link>https://github.com/jacred-fdb/jacred</link>
    <language>ru-RU</language>
    <type>public</type>
  </indexer>");

            foreach (var tracker in trackers ?? Enumerable.Empty<string>())
            {
                var escaped = EscapeXml(tracker);
                sb.Append($@"
  <indexer id=""{escaped}"" configured=""true"">
    <title>{escaped}</title>
    <description>JacRed tracker: {escaped}</description>
    <link>https://github.com/jacred-fdb/jacred</link>
    <language>ru-RU</language>
    <type>public</type>
  </indexer>");
            }

            return sb.Append("\n</indexers>").ToString();
        }

        public static string WrapRss(string itemsXml, string siteOrigin, string torznabApiUrl)
        {
            var site = EscapeXml(siteOrigin.TrimEnd('/'));
            var api = EscapeXml(torznabApiUrl.TrimEnd('/'));
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:atom=""http://www.w3.org/2005/Atom"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <atom:link href=""{api}"" rel=""self"" type=""application/rss+xml"" />
        <title>JacRed</title>
        <description>Torznab API</description>
        <link>{site}/</link>
        <language>en-us</language>
        <category>search</category>
        {itemsXml}
    </channel>
</rss>";
        }

        public static string ItemsXml(IEnumerable<Result> items, string assignedCat, bool enrichTitles, string catParam)
        {
            var sb = new StringBuilder();
            foreach (var t in items)
                sb.Append(ItemXml(t, assignedCat, enrichTitles, catParam));
            return sb.ToString();
        }

        static string ItemXml(Result torrent, string assignedCat, bool enrichTitles, string catParam)
        {
            string title = torrent.Title ?? "Unknown";
            var voices = torrent.info?.voices?.ToList() ?? new List<string>();
            string displayTitle = enrichTitles && voices.Count > 0
                ? $"{title} | [{string.Join(' ', voices)}].rus"
                : title;

            string magnet = torrent.MagnetUri ?? torrent.Details ?? "";
            string detailsUrl = torrent.Details;
            string indexer = torrent.Tracker ?? "JacRed";
            int seeders = torrent.Seeders;
            int leechers = torrent.Peers;
            int peers = leechers > 0 ? seeders + leechers : seeders;
            string itemCat = assignedCat;
            if (string.IsNullOrEmpty(itemCat) && torrent.Category != null && torrent.Category.Count > 0)
                itemCat = torrent.Category.First().ToString();
            if (string.IsNullOrEmpty(itemCat) && !string.IsNullOrWhiteSpace(catParam))
                itemCat = catParam.Split(',')[0].Trim();
            if (string.IsNullOrEmpty(itemCat)) itemCat = "2000";

            string infohash = ExtractInfoHash(magnet);
            string guid = infohash ?? Md5(displayTitle);
            var (season, episode) = SeasonEpisodeFilter.AttrsFromResult(torrent);
            long sizeBytes = ResolveSizeBytes(torrent);
            string pubDate = FormatPubDate(torrent.PublishDate);
            string enclosureType = magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
                ? "application/x-bittorrent;x-scheme-handler/magnet"
                : "application/x-bittorrent";

            var attrs = new StringBuilder();
            AppendAttr(attrs, "magneturl", magnet);
            AppendAttr(attrs, "size", sizeBytes);
            AppendAttr(attrs, "seeders", seeders);
            if (leechers > 0)
                AppendAttr(attrs, "leechers", leechers);
            AppendAttr(attrs, "peers", peers);
            AppendAttr(attrs, "infohash", infohash);
            AppendAttr(attrs, "downloadvolumefactor", "1");
            AppendAttr(attrs, "uploadvolumefactor", "1");
            AppendAttr(attrs, "site", indexer);
            AppendAttr(attrs, "category", itemCat);

            string langTag = Cyrillic.IsMatch(title) ? "ru-RU" : (Latin.IsMatch(title) ? "en-US" : "ru-RU");
            string langCode = langTag.StartsWith("en") ? "en" : "ru";
            AppendAttr(attrs, "language", langTag);
            AppendAttr(attrs, "lang", langCode);

            if (torrent.info?.relased > 0)
                AppendAttr(attrs, "year", torrent.info.relased);
            if (season.HasValue)
                AppendAttr(attrs, "season", season.Value);
            if (episode.HasValue)
            {
                AppendAttr(attrs, "ep", episode.Value);
                AppendAttr(attrs, "episode", episode.Value);
            }

            string comments = !string.IsNullOrWhiteSpace(detailsUrl) && detailsUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? $"\n        <comments>{EscapeXml(detailsUrl)}</comments>"
                : "";

            return $@"
    <item>
        <title>{EscapeXml(displayTitle)}</title>
        <guid isPermaLink=""false"">{guid}</guid>
        <jackettindexer id=""all"">{EscapeXml(indexer)}</jackettindexer>
        <link>{EscapeXml(magnet)}</link>{comments}
        <pubDate>{pubDate}</pubDate>
        <category>{itemCat}</category>
        <size>{sizeBytes}</size>
        <enclosure url=""{EscapeXml(magnet)}"" length=""{sizeBytes}"" type=""{enclosureType}"" />
{attrs}    </item>";
        }

        static void AppendAttr(StringBuilder sb, string name, object value)
        {
            if (value == null)
                return;
            string text = value.ToString();
            if (string.IsNullOrEmpty(text))
                return;
            sb.Append("        <torznab:attr name=\"")
              .Append(name)
              .Append("\" value=\"")
              .Append(EscapeXml(text))
              .Append("\" />\n");
        }

        public static string TryExtractInfoHash(string magnet) => ExtractInfoHash(magnet);

        public static long GetSizeBytes(Result torrent) => ResolveSizeBytes(torrent);

        public static string StableGuid(string input) => Md5(input);

        static string ExtractInfoHash(string magnet)
        {
            if (string.IsNullOrWhiteSpace(magnet))
                return null;

            try
            {
                var m = InfoHashFromMagnet.Match(magnet);
                if (m.Success)
                    return m.Groups[1].Value.ToLowerInvariant();
            }
            catch { }

            return null;
        }

        static string FormatPubDate(DateTime publishDate)
        {
            if (publishDate == default || publishDate.Year < 2000)
                publishDate = DateTime.UtcNow;
            return publishDate.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture);
        }

        static long ResolveSizeBytes(Result torrent)
        {
            if (torrent == null)
                return 0;

            if (torrent.Size > 0)
                return (long)torrent.Size;

            return ParseSizeNameToBytes(torrent.info?.sizeName);
        }

        static long ParseSizeNameToBytes(string sizeName)
        {
            if (string.IsNullOrWhiteSpace(sizeName))
                return 0;

            var match = Regex.Match(sizeName, @"([0-9\.,]+)\s*(Mb|МБ|GB|ГБ|TB|ТБ|KB|КБ|B|Б)?", RegexOptions.IgnoreCase);
            if (!match.Success || !double.TryParse(match.Groups[1].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double value) || value <= 0)
                return 0;

            string unit = match.Groups[2].Value.ToLowerInvariant();
            return unit switch
            {
                "kb" or "кб" => (long)(value * 1024),
                "mb" or "мб" => (long)(value * 1048576),
                "gb" or "гб" => (long)(value * 1073741824),
                "tb" or "тб" => (long)(value * 1099511627776),
                "b" or "б" or "" => (long)value,
                _ => (long)(value * 1048576),
            };
        }

        static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return WebUtility.HtmlEncode(value);
        }

        static string Md5(string input)
        {
            using var md5 = MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""))).Replace("-", "").ToLowerInvariant();
        }
    }
}
