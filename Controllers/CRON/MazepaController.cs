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
using System.Net;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/mazepa/[action]")]
    public class MazepaController : BaseController
    {
        static readonly Dictionary<string, string[]> Categories = new()
        {
            { "37", new[] { "movie" } }
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

        async Task<(int found, int added, string signature)> ParseCategory(string url, string[] types, string host)
        {
            string html = await HttpClient.Get(url, cookie: Cookie(memoryCache));
            if (string.IsNullOrEmpty(html)) return (0, 0, null);

            var rows = Regex.Matches(html, @"<tr id=""tr-(\d+)"".*?>.*?</tr>",
                RegexOptions.Singleline);

            if (rows.Count == 0) return (0, 0, null);

            var list = new List<TorrentDetails>();

            foreach (Match row in rows)
            {
                string block = row.Value;

                string tid = Regex.Match(block, @"tr-(\d+)").Groups[1].Value;
                if (string.IsNullOrEmpty(tid)) continue;

                string title = Regex.Match(block,
                    @"class=""torTopic[^""]*""><b>([^<]+)</b>").Groups[1].Value;

                string magnet = Regex.Match(block,
                    @"href=""(magnet:\?[^""]+)""").Groups[1].Value;

                string size = Regex.Match(block,
                    @">([\d\.,]+)\s*&nbsp;(GB|MB|TB)<").Groups[1].Value + " " +
                              Regex.Match(block,
                    @">([\d\.,]+)\s*&nbsp;(GB|MB|TB)<").Groups[2].Value;

                string _sid = Regex.Match(block,
                    @"seedmed[^>]*><b>(\d+)</b>").Groups[1].Value;

                string _pir = Regex.Match(block,
                    @"leechmed[^>]*><b>(\d+)</b>").Groups[1].Value;

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(magnet))
                    continue;

                int.TryParse(_sid, out int sid);
                int.TryParse(_pir, out int pir);

                list.Add(new TorrentDetails()
                {
                    trackerName = "mazepa",
                    types = types,
                    url = $"{host}/viewtopic.php?t={tid}", // ✅ КЛЮЧ
                    title = title.Trim(),
                    name = tParse.ReplaceBadNames(title),
                    magnet = NormalizeMagnet(magnet),
                    sizeName = size,
                    sid = sid,
                    pir = pir,
                    createTime = DateTime.UtcNow
                });
            }

            list = list.GroupBy(x => x.url).Select(g => g.First()).ToList();
            string signature = string.Join(",", list.Take(5).Select(x => x.url));

            int added = 0;

            await FileDB.AddOrUpdate(list, (t, db) =>
            {
                added++;
                return Task.FromResult(true);
            });

            return (list.Count, added, signature);
        }
    }
}