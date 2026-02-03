using System;
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
using MonoTorrent;
using System.Net;
using System.Web;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/mazepa/[action]")]
    public class MazepaController : BaseController
    {
        static readonly Dictionary<string, string[]> Categories = new()
        {
            { "37", new[] { "movie", "ua", "hd", "uhd" } },
            { "7",  new[] { "movie", "ua", "sd" } },
            { "175", new[] { "movie", "ua", "new" } },
            { "147", new[] { "movie", "uhd" } },
            { "12",  new[] { "movie", "hd" } },
            { "13",  new[] { "movie", "sd" } },
            { "174", new[] { "movie", "sub" } },
            { "38", new[] { "serial", "ua", "hd", "uhd" } },
            { "8",  new[] { "serial", "ua", "sd" } },
            { "152", new[] { "serial", "uhd" } },
            { "44",  new[] { "serial", "hd" } },
            { "14",  new[] { "serial", "sd" } },
            { "35", new[] { "multfilm", "ua", "hd" } },
            { "5",  new[] { "multfilm", "ua", "sd" } },
            { "155", new[] { "multfilm", "uhd" } },
            { "41",  new[] { "multfilm", "hd" } },
            { "10",  new[] { "multfilm", "sd" } },
            { "36", new[] { "multserial", "ua" } },
            { "6",  new[] { "multserial", "ua", "sd" } },
            { "43", new[] { "multserial", "hd" } },
            { "11",  new[] { "multserial", "sd" } },
            { "16", new[] { "anime" } },
            { "39", new[] { "documovie", "ua" } },
            { "9",  new[] { "documovie", "ua", "sd" } },
            { "157", new[] { "documovie", "uhd" } },
            { "42",  new[] { "documovie", "hd" } },
            { "15",  new[] { "documovie", "sd" } }
        };

        static bool _workParse = false;

        static string Cookie(IMemoryCache memoryCache)
            => memoryCache.TryGetValue("cron:MazepaController:Cookie", out string cookie) ? cookie : null;

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
                client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0");
                client.DefaultRequestHeaders.Add("Referer", $"{host}/login.php");

                var data = new Dictionary<string, string>
                {
                    { "login_username", login },
                    { "login_password", pass },
                    { "autologin", "on" },
                    { "redirect", "/index.php" },
                    { "login", "Увійти" }
                };

                var response = await client.PostAsync($"{host}/login.php", new System.Net.Http.FormUrlEncodedContent(data));

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
                    while (true)
                    {
                        await Task.Delay(1200);

                        string url = $"{host}/viewforum.php?f={cat.Key}&start={start}";
                        int added = await ParseCategory(url, cat.Value, host);

                        if (added == 0) break;

                        total += added;
                        start += 50;
                    }
                }

                ParserLog.Write("mazepa", $"Finished: {total} in {sw.Elapsed}");
                return $"ok {total}";
            }
            finally { _workParse = false; }
        }

        async Task<int> ParseCategory(string url, string[] types, string host)
        {
            string html = await HttpClient.Get(url, cookie: Cookie(memoryCache));
            if (string.IsNullOrEmpty(html)) return 0;

            var rg = new Regex(@"href=""topic-[^""]*t=(\d+)\.html""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase);
            var list = new List<TorrentDetails>();

            foreach (Match m in rg.Matches(html))
            {
                string tid = m.Groups[1].Value;
                string title = Regex.Replace(WebUtility.HtmlDecode(m.Groups[2].Value), "<.*?>", "").Trim();
                if (title.Length < 3) continue;

                list.Add(new TorrentDetails
                {
                    trackerName = "mazepa",
                    url = $"{host}/viewtopic.php?t={tid}",
                    title = title,
                    name = tParse.ReplaceBadNames(title),
                    types = types,
                    createTime = DateTime.Now
                });
            }

            if (list.Count == 0) return 0;

            int added = 0;

            await FileDB.AddOrUpdate(list, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out var old) && !string.IsNullOrEmpty(old.magnet))
                    return false;

                await Task.Delay(300);

                string htmlTopic = await HttpClient.Get(t.url, cookie: Cookie(memoryCache));
                if (string.IsNullOrEmpty(htmlTopic)) return false;

                var mag = Regex.Match(htmlTopic, @"href=""(magnet:\?xt=urn:btih:[^""]+)""");
                if (!mag.Success) return false;

                t.magnet = WebUtility.HtmlDecode(mag.Groups[1].Value);

                var size = Regex.Match(htmlTopic, @"(\d+(?:[\.,]\d+)?)\s*(GB|MB|TB)", RegexOptions.IgnoreCase);
                if (size.Success)
                    t.sizeName = $"{size.Groups[1]} {size.Groups[2]}".Replace(",", ".");

                if (htmlTopic.Contains("2160p")) t.quality = 2160;
                else if (htmlTopic.Contains("1080p")) t.quality = 1080;
                else if (htmlTopic.Contains("720p")) t.quality = 720;

                added++;
                return true;
            });

            return added;
        }
    }
}