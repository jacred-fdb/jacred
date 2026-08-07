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

namespace JacRed.Infrastructure.Trackers.Rutracker
{
    public class RutrackerSyncService
    {
        const string TrackerName = "rutracker";
        const string TaskParsePath = "Data/temp/rutracker_taskParse.json";

        readonly IMemoryCache _memoryCache;

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static string Cookie;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerWorkFlag _updateTasksWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static RutrackerSyncService()
        {
            if (IO.File.Exists(TaskParsePath))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText(TaskParsePath));
        }

        static void PersistTaskParse()
        {
            try { IO.File.WriteAllText(TaskParsePath, JsonConvert.SerializeObject(taskParse)); }
            catch { }
        }

        public RutrackerSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        async ValueTask<bool> TakeLogin()
        {
            string authKey = "rutracker:TakeLogin()";
            if (_memoryCache.TryGetValue(authKey, out _))
                return false;

            _memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(2));

            try
            {
                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                    var postParams = new Dictionary<string, string>
                    {
                        { "login_username", AppInit.conf.Rutracker.login.u },
                        { "login_password", AppInit.conf.Rutracker.login.p },
                        { "login", "Вход" }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{AppInit.conf.Rutracker.rqHost()}/forum/login.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string session = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("bb_session="))
                                        session = new Regex("bb_session=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(session))
                                {
                                    Cookie = $"bb_ssl=1; bb_session={session};";
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        public async Task<string> ParseAsync(int page, string cat = null, int maxTopics = 0)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = $"{AppInit.conf.Rutracker.rqHost()}/forum/viewforum.php";

                    var catFilter = string.IsNullOrWhiteSpace(cat)
                        ? null
                        : new HashSet<string>(
                            cat.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                            StringComparer.OrdinalIgnoreCase);

                    string[] cats;
                    if (catFilter == null)
                    {
                        cats = RutrackerCategories.QuickParseIds.ToArray();
                    }
                    else
                    {
                        cats = RutrackerCategories.QuickParseIds.Where(c => catFilter.Contains(c)).ToArray();
                        // Smoke: allow any known forum id, not only QuickParse.
                        if (cats.Length == 0)
                            cats = RutrackerCategories.Ids.Where(c => catFilter.Contains(c)).ToArray();
                    }

                    ParserLog.Write(TrackerName, $"Starting parse page={page}, cats={cats.Length}, maxTopics={maxTopics}, base: {baseUrl}");
                    foreach (string c in cats)
                    {
                        string pageUrl = page == 0 ? $"{baseUrl}?f={c}" : $"{baseUrl}?f={c}&start={page * 50}";
                        ParserLog.Write(TrackerName, $"Category {c}: {pageUrl}");
                        bool result = await parsePage(c, page, maxTopics: maxTopics);
                        log += $"{c} - {page} - {result}\n";
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

        public Task<string> UpdateTasksParseAsync()
        {
            return Task.FromResult(TrackerSyncHelpers.RunUpdateTasksParseInBackground(TrackerName, _updateTasksWork, checkDisabled: false, async ct =>
            {
                foreach (string cat in RutrackerCategories.Ids)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        // Получаем html
                        string html = await HttpClient.Get($"{AppInit.conf.Rutracker.rqHost()}/forum/viewforum.php?f={cat}", useproxy: AppInit.conf.Rutracker.useproxy, cancellationToken: ct);
                        if (html == null)
                            continue;

                        // Максимальное количиство страниц
                        int.TryParse(Regex.Match(html, "Страница <b>1</b> из <b>([0-9]+)</b>").Groups[1].Value, out int maxpages);

                        if (maxpages > 0)
                        {
                            // Загружаем список страниц в список задач
                            for (int page = 0; page <= maxpages; page++)
                            {
                                if (!taskParse.ContainsKey(cat))
                                    taskParse.Add(cat, new List<TaskParse>());

                                var val = taskParse[cat];
                                if (val.FirstOrDefault(i => i.page == page) == null)
                                    val.Add(new TaskParse(page));
                            }
                        }
                        else
                        {
                            if (!taskParse.ContainsKey(cat))
                                taskParse.Add(cat, new List<TaskParse>());

                            var val = taskParse[cat];
                            if (val.FirstOrDefault(i => i.page == 1) == null)
                                val.Add(new TaskParse(1));
                        }
                    }
                    catch { }
                }

                PersistTaskParse();
            }));
        }

        public Task<string> ParseAllTaskAsync()
        {
            return Task.FromResult(TrackerSyncHelpers.RunParseAllTaskInBackground(TrackerName, _parseAllTaskWork, checkDisabled: false, async ct =>
            {
                try
                {
                    var pending = taskParse.ToArray()
                        .SelectMany(t => t.Value.Where(v => DateTime.Today != v.updateTime).Select(v => (cat: t.Key, val: v)))
                        .ToArray();
                    int done = 0;
                    TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", 0, pending.Length);

                    foreach (var item in pending)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(AppInit.conf.Rutracker.parseDelay, ct);

                        bool res = await parsePage(item.cat, item.val.page, ct);
                        if (res)
                            item.val.updateTime = DateTime.Today;

                        done++;
                        TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", done, pending.Length, $"{item.cat}/{item.val.page}");
                    }
                }
                finally
                {
                    PersistTaskParse();
                }
            }));
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

                    foreach (var task in taskParse.ToArray())
                    {
                        var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();

                        foreach (var val in pagesToParse)
                        {
                            await Task.Delay(AppInit.conf.Rutracker.parseDelay);

                            bool res = await parsePage(task.Key, val.page);
                            if (res)
                            {
                                val.updateTime = DateTime.Today;
                                log.AppendLine($"{task.Key} - {val.page}");
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

        async Task<bool> parsePage(string cat, int page, CancellationToken cancellationToken = default, int maxTopics = 0)
        {
            #region Авторизация
            //if (Cookie == null)
            //{
            //    if (await TakeLogin() == false)
            //        return false;
            //}
            #endregion

            string html = await HttpClient.Get($"{AppInit.conf.Rutracker.rqHost()}/forum/viewforum.php?f={cat}{(page == 0 ? "" : $"&start={page * 50}")}", /*cookie: Cookie, */useproxy: AppInit.conf.Rutracker.useproxy, cancellationToken: cancellationToken);
            if (html == null /*|| !html.Contains("id=\"logged-in-username\"")*/)
                return false;

            var torrents = RutrackerParser.ParseTorrentsFromPage(html, cat);
            if (maxTopics > 0 && torrents.Count > maxTopics)
                torrents = torrents.Take(maxTopics).ToList();

            int topicsDone = 0;
            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (maxTopics > 0 && topicsDone >= maxTopics)
                    return true;

                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                var fullNews = await HttpClient.Get(AppInit.conf.Rutracker.rqHost(t.url), useproxy: AppInit.conf.Rutracker.useproxy, cancellationToken: cancellationToken);
                bool ok = RutrackerParser.ApplyTopicPageDetails(t, fullNews);
                if (ok)
                    topicsDone++;
                return ok;
            });

            return torrents.Count > 0;
        }
    }
}
