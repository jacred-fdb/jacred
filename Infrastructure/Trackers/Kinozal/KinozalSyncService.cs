using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.Kinozal
{
    public class KinozalSyncService
    {
        const string TrackerName = "kinozal";

        readonly IMemoryCache _memoryCache;

        static Dictionary<string, Dictionary<string, List<TaskParse>>> taskParse = new Dictionary<string, Dictionary<string, List<TaskParse>>>();

        static string Cookie;

        static readonly Encoding PageEncoding = Encoding.GetEncoding(1251);
        static readonly SemaphoreSlim _loginSemaphore = new SemaphoreSlim(1, 1);
        static readonly Regex RegexCookieValue = new Regex("([^;]+)(;|$)", RegexOptions.Compiled);

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static readonly List<string> Categories = new List<string>()
        {
            // Сериалы
            "45", "46",

            // Фильмы
            "8", "6", "15", "17", "35", "39", "13", "14", "24", "11", "9", "47", "18", "37", "12", "10", "7", "16",

            // ТВ-шоу
            "49", "50",

            // Мульты
            "21", "22",

            // Аниме
            "20"
        };

        static KinozalSyncService()
        {
            if (IO.File.Exists("Data/temp/kinozal_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<TaskParse>>>>(IO.File.ReadAllText("Data/temp/kinozal_taskParse.json"));
        }

        public KinozalSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        static bool IsValidBrowsePage(string html) =>
            !string.IsNullOrWhiteSpace(html)
            && html.Contains("class=\"t_peer\"")
            && html.Contains("details.php?id=")
            && (html.Contains("Кинозал.GURU</title>") || html.Contains("Кинозал.ТВ</title>") || html.Contains("::"));

        static string CookieHeader()
        {
            if (!string.IsNullOrWhiteSpace(AppInit.conf.Kinozal.cookie))
                return AppInit.conf.Kinozal.cookie;

            return Cookie;
        }

        static string ExtractCookieValue(IEnumerable<string> cookieHeaders, string cookieName)
        {
            string cookieKey = $"{cookieName}=";
            string candidate = (cookieHeaders ?? Enumerable.Empty<string>())
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && line.Contains(cookieKey));

            if (candidate == null)
                return null;

            int start = candidate.IndexOf(cookieKey, StringComparison.Ordinal) + cookieKey.Length;
            var match = RegexCookieValue.Match(candidate.Substring(start));
            return match.Success ? match.Groups[1].Value : null;
        }

        async Task<bool> TakeLogin()
        {
            if (!string.IsNullOrWhiteSpace(CookieHeader()))
                return true;

            await _loginSemaphore.WaitAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(CookieHeader()))
                    return true;

                string authKey = "kinozal:TakeLogin()";
                if (_memoryCache.TryGetValue(authKey, out _))
                    return !string.IsNullOrWhiteSpace(Cookie);

                if (string.IsNullOrWhiteSpace(AppInit.conf.Kinozal.login?.u) ||
                    string.IsNullOrWhiteSpace(AppInit.conf.Kinozal.login?.p))
                {
                    ParserLog.Write(TrackerName, "TakeLogin failed: credentials not configured");
                    return false;
                }

                try
                {
                    var clientHandler = new System.Net.Http.HttpClientHandler()
                    {
                        AllowAutoRedirect = false,
                        UseCookies = false
                    };

                    clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                    using (var client = new System.Net.Http.HttpClient(clientHandler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        client.MaxResponseContentBufferSize = 2000000; // 2MB
                        client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36");
                        client.DefaultRequestHeaders.Add("cache-control", "no-cache");
                        client.DefaultRequestHeaders.Add("dnt", "1");
                        client.DefaultRequestHeaders.Add("origin", AppInit.conf.Kinozal.host);
                        client.DefaultRequestHeaders.Add("pragma", "no-cache");
                        client.DefaultRequestHeaders.Add("referer", $"{AppInit.conf.Kinozal.host}/");
                        client.DefaultRequestHeaders.Add("upgrade-insecure-requests", "1");

                        var postParams = new Dictionary<string, string>
                        {
                            { "username", AppInit.conf.Kinozal.login.u },
                            { "password", AppInit.conf.Kinozal.login.p },
                            { "returnto", "" }
                        };

                        using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                        using (var response = await client.PostAsync($"{AppInit.conf.Kinozal.host}/takelogin.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string uid = ExtractCookieValue(cook, "uid");
                                string pass = ExtractCookieValue(cook, "pass");

                                if (!string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(pass))
                                {
                                    Cookie = $"uid={uid}; pass={pass};";
                                    _memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(2));
                                    ParserLog.Write(TrackerName, $"TakeLogin OK uid={uid}");
                                    return true;
                                }
                            }

                            ParserLog.Write(TrackerName, $"TakeLogin failed: no uid/pass cookies, status={(int)response.StatusCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"TakeLogin error: {ex.Message}");
                }

                return false;
            }
            finally
            {
                _loginSemaphore.Release();
            }
        }

        async Task<bool> EnsureLoggedIn()
        {
            if (!string.IsNullOrWhiteSpace(CookieHeader()))
                return true;

            return await TakeLogin();
        }

        async Task<string> GetBrowseHtml(string browseUrl)
        {
            return await HttpClient.Get(
                browseUrl,
                encoding: PageEncoding,
                cookie: CookieHeader(),
                referer: $"{AppInit.conf.Kinozal.host}/",
                useproxy: AppInit.conf.Kinozal.useproxy);
        }

        public async Task<string> ParseAsync(int page)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = $"{AppInit.conf.Kinozal.host}/browse.php";
                    ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                    foreach (string cat in Categories)
                    {
                        string pageUrl = $"{baseUrl}?c={cat}&page={page}";
                        ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                        await parsePage(cat, page);
                        log += $"{cat} - {page}\n";
                    }
                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            });
        }

        public async Task<string> UpdateTasksParseAsync()
        {
            if (!await EnsureLoggedIn())
                return "login failed";

            foreach (string cat in Categories)
            {
                for (int year = DateTime.Today.Year; year >= 1990; year--)
                {
                    // Получаем html
                    string html = await GetBrowseHtml($"{AppInit.conf.Kinozal.host}/browse.php?c={cat}&d={year}&t=1");
                    if (!IsValidBrowsePage(html))
                        continue;

                    // Максимальное количиство страниц
                    int.TryParse(Regex.Match(html, ">([0-9]+)</a></li><li><a rel=\"next\"").Groups[1].Value, out int maxpages);

                    // Загружаем список страниц в список задач
                    for (int page = 0; page <= maxpages; page++)
                    {
                        try
                        {
                            if (!taskParse.ContainsKey(cat))
                                taskParse.Add(cat, new Dictionary<string, List<TaskParse>>());

                            string arg = $"&d={year}&t=1";
                            var catVal = taskParse[cat];
                            if (!catVal.ContainsKey(arg))
                                catVal.Add(arg, new List<TaskParse>());

                            var val = catVal[arg];
                            if (val.FirstOrDefault(i => i.page == page) == null)
                                val.Add(new TaskParse(page));
                        }
                        catch { }
                    }
                }
            }

            IO.File.WriteAllText("Data/temp/kinozal_taskParse.json", JsonConvert.SerializeObject(taskParse));
            return "ok";
        }

        public async Task<string> ParseAllTaskAsync()
        {
            return await TrackerSyncHelpers.RunParseAllTaskAsync(TrackerName, _parseAllTaskWork, checkDisabled: false, async () =>
            {
                foreach (var cat in taskParse.ToArray())
                {
                    foreach (var arg in cat.Value.ToArray())
                    {
                        foreach (var val in arg.Value.ToArray())
                        {
                            if (DateTime.Today == val.updateTime)
                                continue;

                            await Task.Delay(AppInit.conf.Kinozal.parseDelay);

                            bool res = await parsePage(cat.Key, val.page, arg.Key);
                            if (res)
                                val.updateTime = DateTime.Today;
                        }
                    }
                }
            });
        }

        public async Task<string> ParseLatestAsync(int pages = 5)
        {
            return await TrackerSyncHelpers.RunParseLatestAsync(TrackerName, _parseLatestLock, checkDisabled: false, async () =>
            {
                var log = new StringBuilder();

                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName, $"Starting ParseLatest pages={pages}");

                    foreach (var cat in taskParse.ToArray())
                    {
                        foreach (var arg in cat.Value.ToArray())
                        {
                            var pagesToParse = arg.Value.OrderBy(x => x.page).Take(pages).ToArray();

                            foreach (var val in pagesToParse)
                            {
                                await Task.Delay(AppInit.conf.Kinozal.parseDelay);

                                bool res = await parsePage(cat.Key, val.page, arg.Key);
                                if (res)
                                {
                                    val.updateTime = DateTime.Today;
                                    log.AppendLine($"{cat.Key} - {arg.Key} - {val.page}");
                                }
                            }
                        }
                    }

                    ParserLog.Write(TrackerName, $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"ParseLatest Error: {ex.Message}");
                }

                return log.ToString();
            });
        }

        async Task<bool> parsePage(string cat, int page, string arg = null)
        {
            if (!await EnsureLoggedIn())
                return false;

            string browseUrl = $"{AppInit.conf.Kinozal.host}/browse.php?c={cat}&page={page}" + arg;
            string html = await GetBrowseHtml(browseUrl);
            if (!IsValidBrowsePage(html) || !html.Contains(">Выход</a>"))
            {
                Cookie = null;
                if (!await TakeLogin())
                    return false;

                html = await GetBrowseHtml(browseUrl);
                if (!IsValidBrowsePage(html))
                    return false;
            }

            var torrents = KinozalParser.ParseTorrentsFromPage(html, cat);

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails cached) && ShouldSkipHashFetch(cached, t))
                    return true;

                string id = Regex.Match(t.url, "\\?id=([0-9]+)").Groups[1].Value;
                string srv_details = await HttpClient.Post($"{AppInit.conf.Kinozal.host}/get_srv_details.php?id={id}&action=2", $"id={id}&action=2", CookieHeader(), useproxy: AppInit.conf.Kinozal.useproxy);
                if (srv_details != null)
                {
                    string torrentHash = new Regex("<ul><li>Инфо хеш: +([^<]+)</li>").Match(srv_details).Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(torrentHash))
                    {
                        t.magnet = $"magnet:?xt=urn:btih:{torrentHash}";
                        return true;
                    }
                }

                return false;
            });

            return torrents.Count > 0;
        }

        /// <summary>
        /// Кинозал при добавлении серий/озвучек перехеширует .torrent (новый info hash),
        /// но title в списке часто не меняется — раньше hash не перезапрашивался.
        /// </summary>
        static bool ShouldSkipHashFetch(TorrentDetails cached, TorrentDetails parsed)
        {
            if (string.IsNullOrWhiteSpace(cached.magnet))
                return false;

            if (cached.title != parsed.title)
                return false;

            if (cached.sizeName != parsed.sizeName)
                return false;

            // В колонке даты показывается время последнего обновления раздачи («вчера», «сегодня», …)
            if (parsed.createTime.Date > cached.createTime.Date)
                return false;

            return true;
        }
    }
}
