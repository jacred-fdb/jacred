using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using JacRed.Engine;
using JacRed.Engine.CORE;
using JacRed.Models.Details;
using System.Net;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/mazepa/[action]")]
    public class MazepaController : BaseController
    {
        static readonly Dictionary<string, string[]> Categories = new()
        {
            // Українські фільми
            { "37",  new[] { "movie" } },
            { "7",   new[] { "movie" } },

            // Фільми
            { "175", new[] { "movie" } },
            { "147", new[] { "movie" } },
            { "12",  new[] { "movie" } },
            { "13",  new[] { "movie" } },
            { "174", new[] { "movie" } },

            // Українські серіали
            { "38",  new[] { "serial" } },
            { "8",   new[] { "serial" } },

            // Серіали
            { "152", new[] { "serial" } },
            { "44",  new[] { "serial" } },
            { "14",  new[] { "serial" } },

            // Українські мультфільми
            { "35",  new[] { "multfilm" } },
            { "5",   new[] { "multfilm" } },

            // Мультфільми
            { "155", new[] { "multfilm" } },
            { "41",  new[] { "multfilm" } },
            { "10",  new[] { "multfilm" } },

            // Українські мультсеріали
            { "36",  new[] { "multserial" } },
            { "6",   new[] { "multserial" } },

            // Мультсеріали
            { "43",  new[] { "multserial" } },
            { "11",  new[] { "multserial" } },

            // Аніме
            { "16",  new[] { "anime" } },

            // Українські документальні
            { "39",  new[] { "documovie" } },
            { "9",   new[] { "documovie" } },

            // Документальні
            { "157", new[] { "documovie" } },
            { "42",  new[] { "documovie" } },
            { "15",  new[] { "documovie" } },
        };

        static bool _workParse = false;

        static string Cookie(IMemoryCache memoryCache)
            => memoryCache.TryGetValue("cron:MazepaController:Cookie", out string cookie) ? cookie : null;

        static string CleanTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            string t = title;

            t = Regex.Replace(t, @"\s*\((19|20)\d{2}(\-\d{4})?\)", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(Сезон|Season)\s*\d+.*$", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(S\d{1,2}|E\d{1,2}|S\d{1,2}E\d{1,2})\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(2160p|1080p|720p|480p)\b", "", RegexOptions.IgnoreCase);

            t = Regex.Replace(
                t,
                @"\b(WEB[-\s]?DL|WEB[-\s]?Rip|BDRip|BDRemux|HDRip|BluRay|BRRip|DVDRip|HDTV)\b",
                "",
                RegexOptions.IgnoreCase
            );

            t = Regex.Replace(
                t,
                @"\b(x264|x265|h\.?264|h\.?265|hevc|avc|aac|ac3|dts|ddp?\d\.\d|vc\-?1)\b",
                "",
                RegexOptions.IgnoreCase
            );

            t = Regex.Replace(t, @"[\[\]\|]", " ");
            t = Regex.Replace(t, @"\s{2,}", " ").Trim();

            return t;
        }

        static (string name, string originalname) ParseNamesAdvanced(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return (null, null);

            var m = Regex.Match(title, @"^(.*?)\s*\((\d{4}|\d{4}-\d{4})\)");
            string beforeYear = m.Success ? m.Groups[1].Value : title;

            var parts = Regex
                .Split(beforeYear, @"\s*/\s*")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList();

            if (parts.Count == 0)
                return (null, null);

            string original = parts.LastOrDefault(p => Regex.IsMatch(p, @"[A-Za-z]"));
            string name = parts.FirstOrDefault(p => !Regex.IsMatch(p, @"[A-Za-z]"));

            name ??= parts.First();
            original ??= name;

            name = CleanTitle(name);
            original = CleanTitle(original);

            name = tParse.ReplaceBadNames(name) ?? name;
            original = tParse.ReplaceBadNames(original) ?? original;

            return (name, original);
        }

        async Task<bool> CheckLogin()
        {
            if (Cookie(memoryCache) != null)
                return true;

            return await TakeLogin(memoryCache);
        }

        async Task<bool> TakeLogin(IMemoryCache memoryCache)
        {
            try
            {
                var login = AppInit.conf.Mazepa.login.u;
                var pass = AppInit.conf.Mazepa.login.p;
                var host = AppInit.conf.Mazepa.host;
                if (string.IsNullOrEmpty(host)) return false;

                var handler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                };

                using var client = new System.Net.Http.HttpClient(handler);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                var data = new Dictionary<string, string>
                {
                    { "login_username", login },
                    { "login_password", pass },
                    { "autologin", "on" },
                    { "redirect", "/index.php" },
                    { "login", "Увійти" }
                };

                var response = await client.PostAsync($"{host}/login.php",
                    new System.Net.Http.FormUrlEncodedContent(data));

                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    string cookieStr = string.Join("; ", cookies.Select(c => c.Split(';')[0]));
                    if (cookieStr.Contains("bb_"))
                    {
                        memoryCache.Set("cron:MazepaController:Cookie", cookieStr, TimeSpan.FromHours(2));
                        ParserLog.Write("mazepa", "Login OK");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ParserLog.Write("mazepa", $"Login error: {ex.Message}");
            }

            return false;
        }

        public async Task<string> Parse()
        {
            if (_workParse) return "work";
            if (string.IsNullOrEmpty(AppInit.conf.Mazepa.host)) return "disabled";
            _workParse = true;

            try
            {
                if (!await CheckLogin())
                    return "login error";

                var sw = Stopwatch.StartNew();
                int total = 0;
                string host = AppInit.conf.Mazepa.host;

                foreach (var cat in Categories)
                {
                    int start = 0;
                    int page = 1;
                    string lastSignature = null;

                    while (true)
                    {
                        string url = $"{host}/viewforum.php?f={cat.Key}&start={start}";
                        ParserLog.Write("mazepa", $"Parsing forum {cat.Key} (page {page})");

                        var (found, added, signature) = await ParseCategory(url, cat.Value, host);
                        ParserLog.Write("mazepa", $"Found {found} topics, added {added}");

                        if (found == 0)
                            break;

                        if (signature == lastSignature)
                        {
                            ParserLog.Write("mazepa", $"DUPLICATE PAGE → STOP at {page}");
                            break;
                        }

                        lastSignature = signature;
                        total += added;
                        start += 50;
                        page++;

                        await Task.Delay(800);
                    }
                }

                ParserLog.Write("mazepa", $"Finished: {total} in {sw.Elapsed}");
                return $"ok {total}";
            }
            finally { _workParse = false; }
        }

        static string NormalizeMagnet(string magnet)
        {
            if (string.IsNullOrEmpty(magnet)) return null;
            magnet = WebUtility.HtmlDecode(magnet);

            var m = Regex.Match(magnet, @"btih:([A-Fa-f0-9]{40}|[A-Z2-7]{32})");
            if (!m.Success) return null;

            return $"magnet:?xt=urn:btih:{m.Groups[1].Value}";
        }

        static string ParseSizeName(string block)
        {
            var m = Regex.Match(block, @">([\d\.,]+)\s*&nbsp;(GB|MB|TB)<", RegexOptions.IgnoreCase);
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                return $"{m.Groups[1].Value.Trim()} {m.Groups[2].Value.Trim()}";

            m = Regex.Match(block, @"([\d\.,]+)\s*(GB|MB|TB|ГБ|МБ|ТБ)\b", RegexOptions.IgnoreCase);
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                return $"{m.Groups[1].Value.Trim()} {m.Groups[2].Value.Trim()}";

            return null;
        }

        static double ParseSizeBytes(string sizeName)
        {
            if (string.IsNullOrWhiteSpace(sizeName)) return 0;
            try
            {
                var g = Regex.Match(sizeName, "([0-9\\.,]+)\\s*(Mb|МБ|GB|ГБ|TB|ТБ)", RegexOptions.IgnoreCase).Groups;
                if (string.IsNullOrWhiteSpace(g[2].Value)) return 0;
                if (!double.TryParse(g[1].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double size))
                    return 0;
                string u = g[2].Value.ToLowerInvariant();
                if (u is "gb" or "гб") size *= 1024;
                else if (u is "tb" or "тб") size *= 1048576;
                return size * 1048576;
            }
            catch { return 0; }
        }

        static int ParseQuality(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return 480;
            if (title.Contains("2160p") || Regex.IsMatch(title, "(4k|uhd)", RegexOptions.IgnoreCase)) return 2160;
            if (title.Contains("1080p")) return 1080;
            if (title.Contains("720p")) return 720;
            return 480;
        }

        static string ParseVideotype(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "sdr";
            string lower = title.ToLower();
            if (Regex.IsMatch(lower, @"\bhdr\b|hdr10")) return "hdr";
            return "sdr";
        }

        async Task<(int found, int added, string signature)> ParseCategory(string url, string[] types, string host)
        {
            string html = await HttpClient.Get(url, cookie: Cookie(memoryCache));
            if (string.IsNullOrEmpty(html)) return (0, 0, null);

            var rows = Regex.Matches(html, @"<tr id=""tr-(\d+)"".*?>.*?</tr>", RegexOptions.Singleline);
            if (rows.Count == 0) return (0, 0, null);

            var list = new List<TorrentDetails>();

            foreach (Match row in rows)
            {
                string block = row.Value;

                string tid = Regex.Match(block, @"tr-(\d+)").Groups[1].Value;
                if (string.IsNullOrEmpty(tid)) continue;

                string title = Regex.Match(block, @"class=""torTopic[^""]*""><b>([^<]+)</b>").Groups[1].Value;
                string magnet = Regex.Match(block, @"href=""(magnet:\?[^""]+)""").Groups[1].Value;

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(magnet))
                    continue;

                string sizeName = ParseSizeName(block);

                int.TryParse(Regex.Match(block, @"seedmed[^>]*><b>(\d+)</b>").Groups[1].Value, out int sid);
                int.TryParse(Regex.Match(block, @"leechmed[^>]*><b>(\d+)</b>").Groups[1].Value, out int pir);

                var titleTrim = title.Trim();
                var (name, originalname) = ParseNamesAdvanced(titleTrim);

                list.Add(new TorrentDetails()
                {
                    trackerName = "mazepa",
                    types = types,
                    url = $"{host}/viewtopic.php?t={tid}",
                    title = titleTrim,
                    name = name,
                    originalname = originalname,
                    magnet = NormalizeMagnet(magnet),
                    sizeName = sizeName,
                    size = ParseSizeBytes(sizeName),
                    quality = ParseQuality(titleTrim),
                    videotype = ParseVideotype(titleTrim),
                    sid = sid,
                    pir = pir,
                    createTime = DateTime.UtcNow
                });
            }

            list = list.GroupBy(x => x.url).Select(g => g.First()).ToList();
            string signature = string.Join(",", list.Take(5).Select(x => x.url));

            int added = 0;
            await FileDB.AddOrUpdate(list, (t, db) => { added++; return Task.FromResult(true); });

            return (list.Count, added, signature);
        }
    }
}