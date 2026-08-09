using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Http = JacRed.Infrastructure.Networking.HttpClient;
using IO = System.IO;
using NetHttpClient = System.Net.Http.HttpClient;

namespace JacRed.Infrastructure.Trackers.Korsars
{
    /// <summary>
    /// Korsars sync — login required (bb_data cookie). Listing pages carry inline magnets.
    /// Tasks: UpdateTasksParse / ParseAllTask / ParseLatest (Rutor/Anibelka trio).
    /// Requests use rqHost/alias; FDB urls stay on host.
    /// </summary>
    public class KorsarsSyncService
    {
        const string TrackerName = KorsarsParser.TrackerName;
        const string TaskParsePath = "Data/temp/korsars_taskParse.json";
        const string CookieCacheKey = "cron:KorsarsController:Cookie";
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerWorkFlag _updateTasksWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        readonly IMemoryCache _memoryCache;

        static KorsarsSyncService()
        {
            if (IO.File.Exists(TaskParsePath))
            {
                try
                {
                    taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(
                                    IO.File.ReadAllText(TaskParsePath))
                                ?? new Dictionary<string, List<TaskParse>>();
                }
                catch
                {
                    taskParse = new Dictionary<string, List<TaskParse>>();
                }
            }
        }

        public KorsarsSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        static void PersistTaskParse()
        {
            try
            {
                string dir = IO.Path.GetDirectoryName(TaskParsePath);
                if (!string.IsNullOrEmpty(dir))
                    IO.Directory.CreateDirectory(dir);
                IO.File.WriteAllText(TaskParsePath, JsonConvert.SerializeObject(taskParse));
            }
            catch { }
        }

        /// <summary>Canonical host stored in FDB urls (Go Config.Host).</summary>
        static string CanonicalHost() => (AppInit.conf.Korsars?.host ?? "").TrimEnd('/');

        /// <summary>Request host — alias when set (Go requestHost).</summary>
        static string RequestHost() => (AppInit.conf.Korsars?.rqHost() ?? "").TrimEnd('/');

        string CookieHeader()
        {
            if (_memoryCache.TryGetValue(CookieCacheKey, out string dyn) && !string.IsNullOrWhiteSpace(dyn))
                return dyn;

            return string.IsNullOrWhiteSpace(AppInit.conf.Korsars?.cookie)
                ? null
                : AppInit.conf.Korsars.cookie.Trim();
        }

        void InvalidateCookie()
        {
            _memoryCache.Remove(CookieCacheKey);
        }

        async Task<bool> EnsureLoginAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(CookieHeader()))
                return true;
            return await TakeLoginAsync(cancellationToken);
        }

        async Task<bool> TakeLoginAsync(CancellationToken cancellationToken)
        {
            string host = RequestHost();
            string user = AppInit.conf.Korsars?.login?.u?.Trim();
            string pass = AppInit.conf.Korsars?.login?.p ?? "";
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
            {
                ParserLog.Write(TrackerName, "Login skipped — no host or login configured");
                return false;
            }

            ParserLog.Write(TrackerName, "Attempting login", new Dictionary<string, object>
            {
                { "host", host },
                { "user", user }
            });

            try
            {
                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
                };
                using var client = new NetHttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

                var form = new Dictionary<string, string>
                {
                    { "login_username", user },
                    { "login_password", pass },
                    { "autologin", "1" },
                    { "login", "Вход" }
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, host + "/login.php")
                {
                    Content = new FormUrlEncodedContent(form)
                };
                req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                req.Headers.TryAddWithoutValidation("Referer", host + "/");

                using var resp = await client.SendAsync(req, cancellationToken);
                ParserLog.Write(TrackerName, $"Login response status={(int)resp.StatusCode}");

                if (!resp.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    ParserLog.Write(TrackerName, "Login FAILED — no Set-Cookie");
                    return false;
                }

                string cookieStr = string.Join("; ", cookies.Select(c => c.Split(';')[0]));
                if (!cookieStr.Contains("bb_data", StringComparison.Ordinal))
                {
                    ParserLog.Write(TrackerName, "Login FAILED — no bb_data in cookies");
                    return false;
                }

                _memoryCache.Set(CookieCacheKey, cookieStr, TimeSpan.FromHours(24));
                ParserLog.Write(TrackerName, "Login OK, got bb_data");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ParserLog.Write(TrackerName, $"Login error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Parse one zero-based listing page of every category.</summary>
        public async Task<string> ParseAsync(int page, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                string rqHost = RequestHost();
                string canonHost = CanonicalHost();
                if (string.IsNullOrWhiteSpace(rqHost) || string.IsNullOrWhiteSpace(canonHost))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Korsars.host");
                    return "config missing";
                }

                if (!await EnsureLoginAsync(cancellationToken))
                    return "login failed";

                var log = new StringBuilder();
                try
                {
                    var sw = Stopwatch.StartNew();
                    int totalFetched = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                    ParserLog.Write(TrackerName, "Starting parse", new Dictionary<string, object>
                    {
                        { "page", page },
                        { "host", rqHost },
                        { "categories", KorsarsCategories.Map.Count }
                    });

                    foreach (string cat in KorsarsCategories.Ids)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (AppInit.conf.Korsars.parseDelay > 0)
                            await Task.Delay(AppInit.conf.Korsars.parseDelay, cancellationToken);

                        var (fetched, added, updated, skipped, failed) =
                            await ParseCategoryPageAsync(rqHost, canonHost, cat, page, cancellationToken);

                        totalFetched += fetched;
                        totalAdded += added;
                        totalUpdated += updated;
                        totalSkipped += skipped;
                        totalFailed += failed;
                        log.AppendLine($"{cat} - {page}");

                        ParserLog.Write(TrackerName, "Category page done", new Dictionary<string, object>
                        {
                            { "f", cat },
                            { "page", page },
                            { "fetched", fetched },
                            { "added", added },
                            { "skipped", skipped },
                            { "failed", failed }
                        });
                    }

                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                        new Dictionary<string, object>
                        {
                            { "fetched", totalFetched },
                            { "added", totalAdded },
                            { "updated", totalUpdated },
                            { "skipped", totalSkipped },
                            { "failed", totalFailed }
                        });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (ex is OutOfMemoryException)
                        throw;

                    ParserLog.Write(TrackerName, "Error", new Dictionary<string, object>
                    {
                        { "message", ex.Message },
                        { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                    });
                }

                return log.Length == 0 ? "ok" : log.ToString();
            }, cancellationToken);
        }

        public Task<string> UpdateTasksParseAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TrackerSyncHelpers.RunUpdateTasksParseInBackground(TrackerName, _updateTasksWork, checkDisabled: true, async ct =>
            {
                string rqHost = RequestHost();
                if (string.IsNullOrWhiteSpace(rqHost))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Korsars.host");
                    return;
                }

                if (!await EnsureLoginAsync(ct))
                {
                    ParserLog.Write(TrackerName, "UpdateTasksParse: login failed");
                    return;
                }

                foreach (string cat in KorsarsCategories.Ids)
                {
                    ct.ThrowIfCancellationRequested();

                    string html = await Http.Get(
                        KorsarsParser.ForumUrl(rqHost, cat, 0),
                        encoding: Encoding.UTF8,
                        cookie: CookieHeader(),
                        useproxy: AppInit.conf.Korsars.useproxy,
                        cancellationToken: ct);

                    if (string.IsNullOrEmpty(html))
                    {
                        ParserLog.Write(TrackerName, $"UpdateTasksParse f={cat}: empty response");
                        continue;
                    }

                    if (KorsarsParser.LooksLikeLoginForm(html))
                    {
                        ParserLog.Write(TrackerName, $"UpdateTasksParse f={cat}: login form — invalidating session");
                        InvalidateCookie();
                        continue;
                    }

                    int maxPage = KorsarsParser.LastPageFromHtml(html);
                    if (!taskParse.ContainsKey(cat))
                        taskParse[cat] = new List<TaskParse>();

                    var val = taskParse[cat];
                    for (int page = 0; page <= maxPage; page++)
                    {
                        if (val.FirstOrDefault(i => i.page == page) == null)
                            val.Add(new TaskParse(page));
                    }

                    taskParse[cat] = val.OrderBy(x => x.page).ToList();
                    ParserLog.Write(TrackerName, $"UpdateTasksParse f={cat}: maxPage={maxPage}, total={taskParse[cat].Count}");
                }

                PersistTaskParse();
            }));
        }

        public Task<string> ParseAllTaskAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TrackerSyncHelpers.RunParseAllTaskInBackground(TrackerName, _parseAllTaskWork, checkDisabled: true, async ct =>
            {
                string rqHost = RequestHost();
                string canonHost = CanonicalHost();
                if (string.IsNullOrWhiteSpace(rqHost) || string.IsNullOrWhiteSpace(canonHost))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Korsars.host");
                    return;
                }

                if (!await EnsureLoginAsync(ct))
                {
                    ParserLog.Write(TrackerName, "ParseAllTask: login failed");
                    return;
                }

                if (taskParse.Count == 0)
                    await RebuildTasksAsync(rqHost, ct);

                try
                {
                    var pending = taskParse.ToArray()
                        .SelectMany(t => t.Value.Where(v => DateTime.Today != v.updateTime)
                            .Select(v => (cat: t.Key, val: v)))
                        .ToArray();
                    int done = 0;
                    TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", 0, pending.Length);

                    foreach (var item in pending)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (AppInit.conf.Korsars.parseDelay > 0)
                            await Task.Delay(AppInit.conf.Korsars.parseDelay, ct);

                        try
                        {
                            await ParseCategoryPageAsync(rqHost, canonHost, item.cat, item.val.page, ct);
                            // Empty listings still count as done (Go markTaskToday).
                            item.val.updateTime = DateTime.Today;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ParserLog.Write(TrackerName, $"ParseAllTask f={item.cat} page={item.val.page} error: {ex.Message}");
                        }

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

        public async Task<string> ParseLatestAsync(int pages = 5, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseLatestAsync(TrackerName, _parseLatestLock, checkDisabled: true, async () =>
            {
                string rqHost = RequestHost();
                string canonHost = CanonicalHost();
                if (string.IsNullOrWhiteSpace(rqHost) || string.IsNullOrWhiteSpace(canonHost))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Korsars.host");
                    return "config missing";
                }

                if (!await EnsureLoginAsync(cancellationToken))
                    return "login failed";

                if (pages <= 0)
                    pages = 5;

                if (taskParse.Count == 0)
                    await RebuildTasksAsync(rqHost, cancellationToken);

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
                            cancellationToken.ThrowIfCancellationRequested();
                            if (AppInit.conf.Korsars.parseDelay > 0)
                                await Task.Delay(AppInit.conf.Korsars.parseDelay, cancellationToken);

                            try
                            {
                                await ParseCategoryPageAsync(rqHost, canonHost, task.Key, val.page, cancellationToken);
                                val.updateTime = DateTime.Today;
                                log.AppendLine($"{task.Key} - {val.page}");
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                ParserLog.Write(TrackerName, $"ParseLatest f={task.Key} page={val.page} error: {ex.Message}");
                            }
                        }
                    }

                    PersistTaskParse();
                    ParserLog.Write(TrackerName, $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"ParseLatest Error: {ex.Message}");
                }

                return log.Length == 0 ? "ok" : log.ToString();
            }, cancellationToken);
        }

        async Task RebuildTasksAsync(string rqHost, CancellationToken ct)
        {
            if (!await EnsureLoginAsync(ct))
                return;

            foreach (string cat in KorsarsCategories.Ids)
            {
                ct.ThrowIfCancellationRequested();
                string html = await Http.Get(
                    KorsarsParser.ForumUrl(rqHost, cat, 0),
                    encoding: Encoding.UTF8,
                    cookie: CookieHeader(),
                    useproxy: AppInit.conf.Korsars.useproxy,
                    cancellationToken: ct);

                if (string.IsNullOrEmpty(html) || KorsarsParser.LooksLikeLoginForm(html))
                    continue;

                int maxPage = KorsarsParser.LastPageFromHtml(html);
                if (!taskParse.ContainsKey(cat))
                    taskParse[cat] = new List<TaskParse>();

                var val = taskParse[cat];
                for (int page = 0; page <= maxPage; page++)
                {
                    if (val.FirstOrDefault(i => i.page == page) == null)
                        val.Add(new TaskParse(page));
                }

                taskParse[cat] = val.OrderBy(x => x.page).ToList();
            }

            PersistTaskParse();
        }

        async Task<(int fetched, int added, int updated, int skipped, int failed)> ParseCategoryPageAsync(
            string rqHost, string canonHost, string cat, int page, CancellationToken cancellationToken)
        {
            string listUrl = KorsarsParser.ForumUrl(rqHost, cat, page);
            string listHtml = await Http.Get(
                listUrl,
                encoding: Encoding.UTF8,
                cookie: CookieHeader(),
                useproxy: AppInit.conf.Korsars.useproxy,
                cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(listHtml))
            {
                ParserLog.Write(TrackerName, "Listing fetch failed", new Dictionary<string, object>
                {
                    { "f", cat },
                    { "page", page },
                    { "url", listUrl }
                });
                return (0, 0, 0, 0, 0);
            }

            if (KorsarsParser.LooksLikeLoginForm(listHtml))
            {
                ParserLog.Write(TrackerName, $"cat={cat} page={page} returned login form — invalidating session");
                InvalidateCookie();
                return (0, 0, 0, 0, 0);
            }

            var torrents = KorsarsParser.ParseListingHtml(listHtml, cat, canonHost);
            return await SaveTorrentsAsync(torrents, cancellationToken);
        }

        async Task<(int fetched, int added, int updated, int skipped, int failed)> SaveTorrentsAsync(
            List<TorrentDetails> torrents, CancellationToken cancellationToken)
        {
            int fetched = torrents.Count;
            int added = 0, updated = 0, skipped = 0, failed = 0;

            if (torrents.Count == 0)
                return (0, 0, 0, 0, 0);

            torrents = torrents
                .Where(t => !string.IsNullOrWhiteSpace(t.name) && !string.IsNullOrWhiteSpace(t.magnet))
                .ToList();
            fetched = torrents.Count;
            if (fetched == 0)
                return (0, 0, 0, 0, 0);

            await FileDB.AddOrUpdate(torrents, async (torrent, db) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(torrent.magnet))
                {
                    failed++;
                    ParserLog.WriteFailed(TrackerName, torrent, "empty magnet");
                    return false;
                }

                bool exists = db.TryGetValue(torrent.url, out TorrentDetails cached);
                if (exists
                    && string.Equals(cached.title?.Trim(), torrent.title?.Trim(), StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(cached.magnet))
                {
                    skipped++;
                    ParserLog.WriteSkipped(TrackerName, cached, "no changes");
                    return false;
                }

                if (exists)
                {
                    updated++;
                    ParserLog.WriteUpdated(TrackerName, torrent, "magnet/title updated");
                }
                else
                {
                    added++;
                    ParserLog.WriteAdded(TrackerName, torrent);
                }

                return true;
            });

            return (fetched, added, updated, skipped, failed);
        }
    }
}
