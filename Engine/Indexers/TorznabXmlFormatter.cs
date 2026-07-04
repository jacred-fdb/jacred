using JacRed.Models.Api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JacRed.Engine.Indexers
{
    public static class TorznabXmlFormatter
    {
        static readonly Regex Cyrillic = new Regex(@"[а-яА-ЯёЁ]");
        static readonly Regex Latin = new Regex(@"[a-zA-Z]");

        public static string CapsXml(string baseUrl)
        {
            var baseEsc = EscapeXml(baseUrl.TrimEnd('/'));
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
  <server version=""1.0"" title=""JacRed"" strapline=""Native Torznab API"" email=""info@localhost"" url=""{baseEsc}/torznab/api""/>
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

        public const string IndexersXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<indexers>
  <indexer id=""all"" configured=""true"">
    <title>JacRed (all trackers)</title>
    <description>Aggregated JacRed search across all configured trackers</description>
    <link>https://github.com/jacred-fdb/jacred</link>
    <language>ru-RU</language>
    <type>public</type>
  </indexer>
</indexers>";

        public static string WrapRss(string itemsXml, string channelLink)
        {
            var link = EscapeXml(channelLink.TrimEnd('/'));
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>JacRed</title>
        <description>Torznab API</description>
        <link>{link}/</link>
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
            string indexer = torrent.Tracker ?? "JacRed";
            int seeders = torrent.Seeders;
            int peers = torrent.Peers > 0 ? torrent.Seeders + torrent.Peers : torrent.Seeders;
            string itemCat = assignedCat;
            if (string.IsNullOrEmpty(itemCat) && torrent.Category != null && torrent.Category.Count > 0)
                itemCat = torrent.Category.First().ToString();
            if (string.IsNullOrEmpty(itemCat) && !string.IsNullOrWhiteSpace(catParam))
                itemCat = catParam.Split(',')[0].Trim();
            if (string.IsNullOrEmpty(itemCat)) itemCat = "2000";

            string infohash = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(magnet) && magnet.Contains("btih:", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(magnet, @"btih:([a-fA-F0-9]+)", RegexOptions.IgnoreCase);
                    if (m.Success) infohash = m.Groups[1].Value;
                }
            }
            catch { }

            string guid = infohash ?? Md5(displayTitle);
            var (season, episode) = SeasonEpisodeFilter.AttrsFromResult(torrent);
            string seasonAttrs = "";
            if (season.HasValue) seasonAttrs += $"\n        <torznab:attr name=\"season\" value=\"{season}\" />";
            if (episode.HasValue) seasonAttrs += $"\n        <torznab:attr name=\"ep\" value=\"{episode}\" />";

            string langTag = Cyrillic.IsMatch(title) ? "ru-RU" : (Latin.IsMatch(title) ? "en-US" : "ru-RU");
            string langCode = langTag.StartsWith("en") ? "en" : "ru";

            return $@"
    <item>
        <title>{EscapeXml(displayTitle)}</title>
        <guid isPermaLink=""false"">{guid}</guid>
        <link>{EscapeXml(magnet)}</link>
        <pubDate>{DateTime.UtcNow:R}</pubDate>
        <category>{itemCat}</category>
        <enclosure url=""{EscapeXml(magnet)}"" length=""{(long)torrent.Size}"" type=""application/x-bittorrent"" />
        <torznab:attr name=""magneturl"" value=""{EscapeXml(magnet)}"" />
        <torznab:attr name=""size"" value=""{(long)torrent.Size}"" />
        <torznab:attr name=""seeders"" value=""{seeders}"" />
        <torznab:attr name=""peers"" value=""{peers}"" />
        <torznab:attr name=""infohash"" value=""{infohash ?? ""}"" />
        <torznab:attr name=""site"" value=""{EscapeXml(indexer)}"" />
        <torznab:attr name=""category"" value=""{itemCat}"" />
        <torznab:attr name=""language"" value=""{langTag}"" />
        <torznab:attr name=""lang"" value=""{langCode}"" />{seasonAttrs}
    </item>";
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
