using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
using HttpClientHandler = System.Net.Http.HttpClientHandler;
using HttpResponseMessage = System.Net.Http.HttpResponseMessage;
using FormUrlEncodedContent = System.Net.Http.FormUrlEncodedContent;
using NetHttpClient = System.Net.Http.HttpClient;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.Kinozal
{
    public class KinozalSyncService
    {
        const string TrackerName = "kinozal";
        const string TaskParsePath = "Data/temp/kinozal_taskParse.json";
        static string CyclePath => ParseAllCycleStore.CyclePathForTracker(TrackerName);

        readonly IMemoryCache _memoryCache;

        static Dictionary<string, Dictionary<string, List<TaskParse>>> taskParse = new Dictionary<string, Dictionary<string, List<TaskParse>>>();

        string _cookie;
        string _lastLoginError;

        static readonly Encoding PageEncoding = Encoding.GetEncoding(1251);
        static readonly SemaphoreSlim _loginSemaphore = new SemaphoreSlim(1, 1);
        static readonly Regex RegexCookieValue = new Regex("([^;]+)(;|$)", RegexOptions.Compiled);

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _cluster = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();
        static int _consecutiveStales;
        const int StaleRecycleAfter = 3;
        const int StaleRetryDelayMs = 3000;
        const int StaleMaxRetries = 3;

        static KinozalSyncService()
        {
            if (IO.File.Exists(TaskParsePath))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<TaskParse>>>>(IO.File.ReadAllText(TaskParsePath));
        }

        static void PersistTaskParse()
        {
            try { ParseAllCycleStore.WriteJsonAtomic(TaskParsePath, taskParse); }
            catch { }
        }

        public KinozalSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        static string TrackerHost()
        {
            try
            {
                return new Uri(AppInit.conf.Kinozal.host).Host;
            }
            catch (UriFormatException)
            {
                return "kinozal.guru";
            }
            catch (ArgumentException)
            {
                return "kinozal.guru";
            }
        }

        static bool TryEnterCluster(string jobLabel)
        {
            if (_cluster.TryStart())
                return true;

            TrackerSyncHelpers.LogParseSkipped(TrackerName, TrackerSyncHelpers.WorkResult);
            ParserLog.Write(TrackerName, $"{jobLabel} skipped: sibling job running");
            return false;
        }

        void NoteValidBrowse() => Interlocked.Exchange(ref _consecutiveStales, 0);

        async Task NoteStaleBrowseAsync()
        {
            int n = Interlocked.Increment(ref _consecutiveStales);
            if (n < StaleRecycleAfter)
                return;

            Interlocked.Exchange(ref _consecutiveStales, 0);
            ParserLog.Write(TrackerName, "recycle FlareSolverr session after consecutive stale shells");
            await CloudflareClearance.RecycleSession(TrackerHost());
        }

        string CookieHeader()
        {
            if (!string.IsNullOrWhiteSpace(AppInit.conf.Kinozal.cookie))
                return AppInit.conf.Kinozal.cookie;

            return _cookie;
        }

        static string ExtractCookieValue(IEnumerable<string> cookieHeaders, string cookieName)
        {
            string cookieKey = $"{cookieName}=";
            foreach (string line in cookieHeaders ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains(cookieKey))
                    continue;

                int start = line.IndexOf(cookieKey, StringComparison.Ordinal) + cookieKey.Length;
                var match = RegexCookieValue.Match(line.Substring(start));
                if (match.Success)
                    return match.Groups[1].Value;
            }

            return null;
        }

        static bool TryBuildCookieFromContainer(CookieContainer cookieJar, Uri hostUri, out string cookieHeader)
        {
            cookieHeader = null;
            if (cookieJar == null || hostUri == null)
                return false;

            string uid = null, pass = null;
            foreach (Cookie cookie in cookieJar.GetCookies(hostUri))
            {
                if (cookie.Name == "uid")
                    uid = cookie.Value;
                if (cookie.Name == "pass")
                    pass = cookie.Value;
            }

            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(pass))
                return false;

            cookieHeader = $"uid={uid}; pass={pass};";
            return true;
        }

        static bool TryBuildCookieFromResponse(HttpResponseMessage response, out string cookieHeader)
        {
            cookieHeader = null;
            if (response == null)
                return false;

            IEnumerable<string> setCookies = null;
            if (response.Headers.TryGetValues("Set-Cookie", out var headerValues))
                setCookies = headerValues;
            else if (response.Headers.NonValidated.TryGetValues("Set-Cookie", out var nonValidatedValues))
                setCookies = nonValidatedValues;

            if (setCookies == null)
                return false;

            string uid = ExtractCookieValue(setCookies, "uid");
            string pass = ExtractCookieValue(setCookies, "pass");
            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(pass))
                return false;

            cookieHeader = $"uid={uid}; pass={pass};";
            return true;
        }

        async Task<bool> TakeLogin()
        {
            if (!string.IsNullOrWhiteSpace(CookieHeader()))
                return true;

            if (!await _loginSemaphore.WaitAsync(TimeSpan.FromSeconds(15)))
            {
                _lastLoginError = "login wait timeout";
                ParserLog.Write(TrackerName, "TakeLogin skipped: login semaphore timeout (15s)");
                return false;
            }
            try
            {
                if (!string.IsNullOrWhiteSpace(CookieHeader()))
                    return true;

                if (string.IsNullOrWhiteSpace(AppInit.conf.Kinozal.login?.u) ||
                    string.IsNullOrWhiteSpace(AppInit.conf.Kinozal.login?.p))
                {
                    _lastLoginError = "credentials not configured (set Kinozal.login.u/p or Kinozal.cookie)";
                    ParserLog.Write(TrackerName, $"TakeLogin failed: {_lastLoginError}");
                    return false;
                }

                string host = AppInit.conf.Kinozal.host?.TrimEnd('/');
                if (string.IsNullOrWhiteSpace(host))
                {
                    _lastLoginError = "host is not configured";
                    ParserLog.Write(TrackerName, $"TakeLogin failed: {_lastLoginError}");
                    return false;
                }

                var cookieJar = new CookieContainer();

                try
                {
                    var hostUri = new Uri(host + "/");
                    var clientHandler = new HttpClientHandler()
                    {
                        AllowAutoRedirect = false,
                        UseCookies = true,
                        CookieContainer = cookieJar
                    };

                    clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                    using (var client = new NetHttpClient(clientHandler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        client.MaxResponseContentBufferSize = 2000000; // 2MB
                        client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36");
                        client.DefaultRequestHeaders.Add("cache-control", "no-cache");
                        client.DefaultRequestHeaders.Add("dnt", "1");
                        client.DefaultRequestHeaders.Add("origin", host);
                        client.DefaultRequestHeaders.Add("pragma", "no-cache");
                        client.DefaultRequestHeaders.Add("referer", $"{host}/");
                        client.DefaultRequestHeaders.Add("upgrade-insecure-requests", "1");

                        var postParams = new Dictionary<string, string>
                        {
                            { "username", AppInit.conf.Kinozal.login.u },
                            { "password", AppInit.conf.Kinozal.login.p },
                            { "returnto", "" }
                        };

                        using (var postContent = new FormUrlEncodedContent(postParams))
                        using (var response = await client.PostAsync($"{host}/takelogin.php", postContent))
                        {
                            string cookieHeader = null;
                            if (!TryBuildCookieFromContainer(cookieJar, hostUri, out cookieHeader))
                                TryBuildCookieFromResponse(response, out cookieHeader);

                            if (!string.IsNullOrWhiteSpace(cookieHeader))
                            {
                                _cookie = cookieHeader;
                                _lastLoginError = null;
                                ParserLog.Write(TrackerName, "TakeLogin OK");
                                return true;
                            }

                            _lastLoginError = $"no uid/pass cookies in response, status={(int)response.StatusCode}";
                            ParserLog.Write(TrackerName, $"TakeLogin failed: {_lastLoginError}");
                        }
                    }
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or UriFormatException or CookieException)
                {
                    _lastLoginError = ex.Message;
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

        async Task<string> GetBrowseHtml(string browseUrl, CancellationToken cancellationToken = default)
        {
            return await HttpClient.Get(
                browseUrl,
                encoding: PageEncoding,
                cookie: CookieHeader(),
                referer: $"{AppInit.conf.Kinozal.host}/",
                useproxy: AppInit.conf.Kinozal.useproxy,
                cancellationToken: cancellationToken);
        }

        async Task<string> GetBrowseHtmlRetryingTransient(string browseUrl, CancellationToken cancellationToken)
        {
            string html = await GetBrowseHtml(browseUrl, cancellationToken);
            if (!KinozalParser.IsTransientBrowseFailure(html))
                return html;

            await Task.Delay(1500, cancellationToken);
            return await GetBrowseHtml(browseUrl, cancellationToken);
        }

        public async Task<string> ParseAsync(int page)
        {
            if (!TryEnterCluster("parse"))
                return TrackerSyncHelpers.WorkResult;

            try
            {
                return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
                {
                    string log = "";

                    try
                    {
                        var sw = Stopwatch.StartNew();
                        string baseUrl = $"{AppInit.conf.Kinozal.host}/browse.php";
                        ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                        foreach (string cat in KinozalCategories.Ids)
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
            finally
            {
                _cluster.End();
            }
        }

        public async Task<string> UpdateTasksParseAsync()
        {
            if (_cluster.IsBusy)
            {
                TrackerSyncHelpers.LogParseSkipped(TrackerName, TrackerSyncHelpers.WorkResult);
                ParserLog.Write(TrackerName, "UpdateTasksParse skipped: sibling job running");
                return TrackerSyncHelpers.WorkResult;
            }

            if (!await EnsureLoggedIn())
                return string.IsNullOrWhiteSpace(_lastLoginError) ? "login failed" : $"login failed: {_lastLoginError}";

            return TrackerSyncHelpers.RunUpdateTasksParseInBackground(
                TrackerName,
                _cluster,
                checkDisabled: false,
                async ct =>
                {
                    int delayMs = KinozalParser.UpdateTasksParseDelayMs(AppInit.conf.Kinozal.parseDelay);
                    int pruned = 0;
                    foreach (string cat in KinozalCategories.Ids)
                    {
                        for (int year = DateTime.Today.Year; year >= 1990; year--)
                        {
                            ct.ThrowIfCancellationRequested();

                            string html = await GetBrowseHtml($"{AppInit.conf.Kinozal.host}/browse.php?c={cat}&d={year}&t=1", ct);
                            if (delayMs > 0)
                                await Task.Delay(delayMs, ct);

                            string yearArg = $"&d={year}&t=1";
                            bool mismatch = KinozalParser.BrowseFiltersMismatch(html, cat, yearArg);
                            if (KinozalParser.IsStaleListingHtml(html) || mismatch)
                                await NoteStaleBrowseAsync();
                            else if (KinozalParser.IsValidBrowsePage(html))
                                NoteValidBrowse();

                            if (mismatch || !KinozalParser.IsValidBrowsePage(html))
                                continue;

                            // Digit before rel=next is 1-based last listing page (URL 0-based).
                            // page <= digit enqueued an empty year tail (~15 KB, «Нет активных раздач»).
                            int pageCount = KinozalParser.YearTaskPageCount(html);
                            try
                            {
                                if (!taskParse.ContainsKey(cat))
                                    taskParse.Add(cat, new Dictionary<string, List<TaskParse>>());

                                string arg = $"&d={year}&t=1";
                                var catVal = taskParse[cat];
                                if (!catVal.ContainsKey(arg))
                                    catVal.Add(arg, new List<TaskParse>());

                                var val = catVal[arg];
                                for (int page = 0; page < pageCount; page++)
                                {
                                    if (val.FirstOrDefault(i => i.page == page) == null)
                                        val.Add(new TaskParse(page));
                                }

                                pruned += KinozalParser.PrunePagesBeyondYearCount(val, pageCount);
                            }
                            catch { }
                        }
                    }

                    PersistTaskParse();
                    if (pruned > 0)
                        ParserLog.Write(TrackerName, $"UpdateTasksParse pruned {pruned} empty year-tail pages");
                },
                TimeSpan.FromHours(2));
        }

        public Task<string> ParseAllTaskAsync()
        {
            return Task.FromResult(TrackerSyncHelpers.RunParseAllTaskInBackground(TrackerName, _cluster, checkDisabled: false, async ct =>
            {
                try
                {
                    var (cycle, mapCount, pendingCount) = ParseAllCycleStore.BeginNestedFullRun(TrackerName, taskParse);
                    ParserLog.Write(TrackerName, $"ParseAllTask start {ParseAllCycleStore.FormatStartLog(cycle, pendingCount, mapCount)}");

                    var pending = taskParse.ToArray()
                        .SelectMany(cat => cat.Value.ToArray()
                            .SelectMany(arg => arg.Value.Where(v => ParseAllCycleStore.IsPendingInCycle(v, cycle))
                                .Select(v => (cat: cat.Key, arg: arg.Key, val: v))))
                        .ToArray();
                    int attempted = 0;
                    int succeeded = 0;
                    TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", 0, pending.Length);

                    foreach (var item in pending)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(AppInit.conf.Kinozal.parseDelay, ct);

                        bool res = await parsePage(item.cat, item.val.page, item.arg, ct);
                        if (res)
                        {
                            ParseAllCycleStore.MarkDoneInCycle(item.val, cycle);
                            ParseAllCycleStore.PersistAfterPage(CyclePath, cycle, TaskParsePath, taskParse, persistCycle: true);
                            succeeded++;
                        }

                        attempted++;
                        TrackerSyncHelpers.ReportProgress(TrackerName, "ParseAllTask", attempted, pending.Length, item.cat, item.val.page);
                        if (attempted == pending.Length || attempted % 25 == 0)
                            ParserLog.Write(TrackerName, $"ParseAllTask ok={succeeded}/{attempted}");
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
            if (!TryEnterCluster("ParseLatest"))
                return TrackerSyncHelpers.WorkResult;

            try
            {
                return await TrackerSyncHelpers.RunParseLatestAsync(TrackerName, _parseLatestLock, checkDisabled: false, async () =>
                {
                    var log = new StringBuilder();

                    try
                    {
                        var sw = Stopwatch.StartNew();
                        ParserLog.Write(TrackerName, $"Starting ParseLatest pages={pages}");

                        var cycle = ParseAllCycleStore.LoadNestedActiveCycle(TrackerName, taskParse);

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
                                        ParseAllCycleStore.MarkDoneInCycle(val, cycle);
                                        log.AppendLine($"{cat.Key} - {arg.Key} - {val.page}");
                                    }
                                }
                            }
                        }

                        PersistTaskParse();
                        ParseAllCycleStore.SaveState(CyclePath, cycle);
                        ParserLog.Write(TrackerName, $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                    }
                    catch (Exception ex)
                    {
                        ParserLog.Write(TrackerName, $"ParseLatest Error: {ex.Message}");
                    }

                    return log.ToString();
                });
            }
            finally
            {
                _cluster.End();
            }
        }

        async Task<bool> parsePage(string cat, int page, string arg = null, CancellationToken cancellationToken = default)
        {
            if (!await EnsureLoggedIn())
                return false;

            string browseUrl = $"{AppInit.conf.Kinozal.host}/browse.php?c={cat}&page={page}" + arg;
            string html = await GetBrowseHtmlRetryingTransient(browseUrl, cancellationToken);

            if (KinozalParser.IsTransientBrowseFailure(html))
            {
                ParserLog.Write(TrackerName, $"browse transient failure, skip login: {browseUrl}");
                return false;
            }

            if (KinozalParser.IsEmptySearchResult(html) && !KinozalParser.BrowseFiltersMismatch(html, cat, arg))
            {
                // page=0 can be a leftover empty-search tab; year tails are really empty.
                if (page == 0)
                {
                    for (int retry = 0;
                         KinozalParser.IsEmptySearchResult(html)
                         && !KinozalParser.BrowseFiltersMismatch(html, cat, arg)
                         && retry < 2;
                         retry++)
                    {
                        await Task.Delay(StaleRetryDelayMs, cancellationToken);
                        html = await GetBrowseHtml(browseUrl, cancellationToken);
                    }
                }

                if (KinozalParser.IsEmptySearchResult(html) && !KinozalParser.BrowseFiltersMismatch(html, cat, arg))
                {
                    ParserLog.Write(TrackerName, $"browse empty search: {browseUrl} {KinozalParser.FormatBrowseDiag(html)}");
                    NoteValidBrowse();
                    return true;
                }
            }

            for (int retry = 0;
                 (KinozalParser.IsStaleListingHtml(html) || KinozalParser.BrowseFiltersMismatch(html, cat, arg))
                 && retry < StaleMaxRetries;
                 retry++)
            {
                await Task.Delay(StaleRetryDelayMs, cancellationToken);
                html = await GetBrowseHtml(browseUrl, cancellationToken);
            }

            if (KinozalParser.IsStaleListingHtml(html) || KinozalParser.IsTransientBrowseFailure(html))
            {
                ParserLog.Write(TrackerName, $"browse stale/empty shell: {browseUrl} {KinozalParser.FormatBrowseDiag(html)}");
                await NoteStaleBrowseAsync();
                return false;
            }

            if (KinozalParser.BrowseFiltersMismatch(html, cat, arg))
            {
                ParserLog.Write(TrackerName, $"browse filter mismatch: {browseUrl} {KinozalParser.FormatBrowseDiag(html)}");
                await NoteStaleBrowseAsync();
                return false;
            }

            if (KinozalParser.IsEmptySearchResult(html))
            {
                ParserLog.Write(TrackerName, $"browse empty search: {browseUrl} {KinozalParser.FormatBrowseDiag(html)}");
                NoteValidBrowse();
                return true;
            }

            NoteValidBrowse();

            if (KinozalParser.IsLoginWall(html) || (KinozalParser.IsValidBrowsePage(html) && !KinozalParser.IsLoggedIn(html)))
            {
                _cookie = null;
                if (!await TakeLogin())
                    return false;

                html = await GetBrowseHtmlRetryingTransient(browseUrl, cancellationToken);
                if (KinozalParser.BrowseFiltersMismatch(html, cat, arg))
                {
                    ParserLog.Write(TrackerName, $"browse filter mismatch: {browseUrl} {KinozalParser.FormatBrowseDiag(html)}");
                    await NoteStaleBrowseAsync();
                    return false;
                }

                if (KinozalParser.IsEmptySearchResult(html))
                {
                    ParserLog.Write(TrackerName, $"browse empty search: {browseUrl} {KinozalParser.FormatBrowseDiag(html)}");
                    NoteValidBrowse();
                    return true;
                }

                if (KinozalParser.IsTransientBrowseFailure(html) || KinozalParser.IsStaleListingHtml(html) || !KinozalParser.IsValidBrowsePage(html))
                {
                    if (KinozalParser.IsStaleListingHtml(html))
                        await NoteStaleBrowseAsync();
                    return false;
                }
            }
            else if (!KinozalParser.IsValidBrowsePage(html))
            {
                return false;
            }

            var torrents = KinozalParser.ParseTorrentsFromPage(html, cat);
            int listingHrefCount = KinozalParser.CountTorrentListingLinks(html);
            if (listingHrefCount > 0 && torrents.Count == 0)
            {
                ParserLog.Write(TrackerName, $"parse yielded 0 from {listingHrefCount} listing hrefs: {browseUrl}");
                return false;
            }

            int resolved = 0;

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails cached) && KinozalParser.ShouldSkipHashFetch(cached, t))
                {
                    resolved++;
                    return true;
                }

                if (!KinozalParser.TryGetDetailsId(t.url, out int id))
                    return false;

                // GET: id/action already in the query. HttpClient.Get routes guarded
                // Cloudflare hosts through FlareSolverr. request.post is flaky in the
                // shared Chromium session (returns the previous browse tab).
                string srv_details = await HttpClient.Get(
                    $"{AppInit.conf.Kinozal.host}/get_srv_details.php?id={id}&action=2",
                    encoding: PageEncoding,
                    cookie: CookieHeader(),
                    useproxy: AppInit.conf.Kinozal.useproxy,
                    cancellationToken: cancellationToken);

                string torrentHash = KinozalParser.ParseInfoHash(srv_details);
                if (string.IsNullOrWhiteSpace(torrentHash))
                    return false;

                t.magnet = $"magnet:?xt=urn:btih:{torrentHash.ToUpperInvariant()}";
                resolved++;
                return true;
            });

            return KinozalParser.ShouldMarkPageDone(torrents.Count, resolved, listingHrefCount);
        }
    }
}
