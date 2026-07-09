using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Engine.CORE;
using JacRed.Engine.Parsing;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Engine.Trackers.Mazepa
{
    public class MazepaSyncService
    {
        const string TrackerName = "mazepa";

        readonly IMemoryCache _memoryCache;

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

        public MazepaSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        string Cookie()
            => _memoryCache.TryGetValue("cron:MazepaController:Cookie", out string cookie) ? cookie : null;

        async Task<bool> CheckLogin()
        {
            if (Cookie() != null)
                return true;

            return await TakeLogin();
        }

        async Task<bool> TakeLogin()
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
                        _memoryCache.Set("cron:MazepaController:Cookie", cookieStr, TimeSpan.FromHours(2));
                        ParserLog.Write(TrackerName, "Login OK");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ParserLog.Write(TrackerName, $"Login error: {ex.Message}");
            }

            return false;
        }

        public async Task<string> ParseAsync()
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
                        ParserLog.Write(TrackerName, $"Parsing forum {cat.Key} (page {page})");

                        var (found, added, signature) = await ParseCategory(url, cat.Value, host);
                        ParserLog.Write(TrackerName, $"Found {found} topics, added {added}");

                        if (found == 0)
                            break;

                        if (signature == lastSignature)
                        {
                            ParserLog.Write(TrackerName, $"DUPLICATE PAGE → STOP at {page}");
                            break;
                        }

                        lastSignature = signature;
                        total += added;
                        start += 50;
                        page++;

                        await Task.Delay(800);
                    }
                }

                ParserLog.Write(TrackerName, $"Finished: {total} in {sw.Elapsed}");
                return $"ok {total}";
            }
            finally { _workParse = false; }
        }

        async Task<(int found, int added, string signature)> ParseCategory(string url, string[] types, string host)
        {
            string html = await HttpClient.Get(url, cookie: Cookie());
            if (string.IsNullOrEmpty(html)) return (0, 0, null);

            var list = MazepaParser.ParseTorrentsFromCategoryPage(html, types, host);
            if (list.Count == 0) return (0, 0, null);

            string signature = string.Join(",", list.Take(5).Select(x => x.url));

            int added = 0;
            await FileDB.AddOrUpdate(list, (torrent, db) =>
            {
                if (db.TryGetValue(torrent.url, out TorrentDetails existing))
                {
                    torrent.createTime = existing.createTime != default ? existing.createTime : torrent.createTime;
                }
                else
                {
                    added++;
                }

                return Task.FromResult(true);
            });

            return (list.Count, added, signature);
        }
    }
}
