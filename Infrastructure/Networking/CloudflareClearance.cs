using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JacRed.Infrastructure.Networking
{
    /// <summary>
    /// Хосты за Cloudflare: браузер FlareSolverr решает задачу и отдаёт
    /// <c>cf_clearance</c>. Дальше страницы берёт <see cref="CfFetch"/>
    /// (localhost cffetch, TLS Chrome). Обычный .NET HttpClient с той же
    /// cookie получает 403. У каждого хоста своя сессия Chromium — иначе
    /// kinozal и anibelka делят вкладку.
    /// </summary>
    public static class CloudflareClearance
    {
        const string SessionPrefix = "jacred";

        sealed class GuardState
        {
            public DateTime Since;

            /// <summary>Когда последний раз давали дешёвому пути шанс.</summary>
            public DateTime LastProbe;
        }

        sealed class BrowserSession
        {
            public readonly string Name;
            public readonly string SolverUrl;
            public readonly SemaphoreSlim Gate = new(1, 1);
            public bool Alive;
            public DateTime LastUse = DateTime.MinValue;
            public int ConsecutiveBrowserTimeouts;

            public BrowserSession(string name, string solverUrl)
            {
                Name = name;
                SolverUrl = solverUrl ?? "";
            }
        }

        static readonly ConcurrentDictionary<string, GuardState> _guarded = new(StringComparer.OrdinalIgnoreCase);
        static readonly ConcurrentDictionary<string, BrowserSession> _sessions = new(StringComparer.Ordinal);

        static readonly AsyncLocal<bool> _crawlLane = new();

        static Timer _idleTimer;

        /// <summary>
        /// Route FlareSolverr calls to <c>flaresolverr.crawlUrl</c> (ParseAll / UpdateTasks / ParseLatest).
        /// No-op when crawlUrl is empty. Must wrap background Task.Run — HTTP middleware
        /// is gone by the time ParseAll actually fetches.
        /// </summary>
        public static IDisposable UseCrawlLane()
        {
            _crawlLane.Value = true;
            return new LaneScope();
        }

        public static bool IsCrawlLane => _crawlLane.Value;

        sealed class LaneScope : IDisposable
        {
            public void Dispose() => _crawlLane.Value = false;
        }

        static FlareSolverrSettingsView Conf
        {
            get
            {
                var c = AppInit.conf?.flaresolverr;

                return c == null || !c.enable || string.IsNullOrWhiteSpace(c.url)
                    ? default
                    : new FlareSolverrSettingsView(c, _crawlLane.Value);
            }
        }

        readonly struct FlareSolverrSettingsView
        {
            public readonly string Url;
            public readonly int MaxTimeoutMs;
            public readonly int SessionIdleMinutes;
            public readonly int BrowserTimeoutRetries;
            public readonly int RecycleAfterTimeouts;
            public readonly int GuardedHours;
            public readonly int RecheckMinutes;

            public FlareSolverrSettingsView(Models.AppConf.FlareSolverrSettings c, bool crawl = false)
                : this(c, crawl && !string.IsNullOrWhiteSpace(c.crawlUrl) ? c.crawlUrl : c.url)
            {
            }

            public FlareSolverrSettingsView(Models.AppConf.FlareSolverrSettings c, string url)
            {
                Url = url;
                MaxTimeoutMs = c.maxTimeoutMs;
                SessionIdleMinutes = c.sessionIdleMinutes;
                BrowserTimeoutRetries = Math.Max(0, c.browserTimeoutRetries);
                RecycleAfterTimeouts = Math.Max(1, c.recycleAfterTimeouts);
                GuardedHours = c.guardedHours;
                RecheckMinutes = c.recheckMinutes;
            }
        }

        /// <summary>
        /// FlareSolverr session id for a host. Safe charset <c>[A-Za-z0-9_-]</c>.
        /// <c>kinozal.guru</c> → <c>jacred-kinozal_guru</c>.
        /// </summary>
        public static string SessionNameFor(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return SessionPrefix;

            var sb = new StringBuilder(SessionPrefix.Length + 1 + host.Length);
            sb.Append(SessionPrefix);
            sb.Append('-');
            foreach (char c in host.Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            return sb.ToString();
        }

        static string SessionKey(string solverUrl, string sessionName)
            => (solverUrl ?? "") + "\n" + sessionName;

        static BrowserSession SessionForHost(string host)
        {
            string name = SessionNameFor(host);
            string url = Conf.Url ?? "";
            return _sessions.GetOrAdd(SessionKey(url, name), _ => new BrowserSession(name, url));
        }

        #region признак «хост за проверкой»

        /// <summary>
        /// Ответ похож на вызов Cloudflare. Признак — <c>cf-mitigated</c>, не <c>cf-ray</c>
        /// (cf-ray стоит на каждом ответе сайта за Cloudflare, включая обычный 200).
        /// </summary>
        public static bool IsChallenge(HttpResponseMessage response)
        {
            if (response == null)
                return false;

            if (response.StatusCode != System.Net.HttpStatusCode.Forbidden &&
                response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable)
                return false;

            return response.Headers.TryGetValues("cf-mitigated", out _);
        }

        /// <summary>
        /// Разметка задачи Cloudflare в теле (старые виды без <c>cf-mitigated</c>).
        ///
        /// Важно: голый <c>challenge-platform</c> нельзя считать проверкой —
        /// на обычных страницах rutracker Cloudflare вшивает
        /// <c>/cdn-cgi/challenge-platform/scripts/jsd/main.js</c>. Это не interstitial.
        /// Ищем именно orchestrate/chl_page или заголовок «Just a moment…».
        /// </summary>
        public static bool IsChallengeBody(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Length > 200_000)
                return false;

            if (body.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase)
                || body.Contains("cf_chl_opt", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Один момент", StringComparison.OrdinalIgnoreCase))
                return true;

            // Реальная задача CF, не jsd/main.js на обычной выдаче.
            return body.Contains("orchestrate/chl_page", StringComparison.OrdinalIgnoreCase)
                || body.Contains("challenge-platform/h/", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsGuarded(string host)
        {
            var conf = Conf;
            if (conf.Url == null || string.IsNullOrWhiteSpace(host))
                return false;

            if (!_guarded.TryGetValue(host, out var state))
                return false;

            var now = DateTime.UtcNow;

            if (now > state.Since.AddHours(conf.GuardedHours))
            {
                _guarded.TryRemove(host, out _);
                return false;
            }

            if (now > state.LastProbe.AddMinutes(conf.RecheckMinutes))
            {
                state.LastProbe = now;
                return false;
            }

            return true;
        }

        public static void Unguard(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            if (_guarded.TryRemove(host, out _))
                JacRedLog.Information(JacRedLogCategories.Host, $"{host} отвечает обычному клиенту, браузер больше не нужен");
        }

        public static void MarkGuarded(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            var now = DateTime.UtcNow;

            if (_guarded.TryGetValue(host, out var state))
            {
                state.Since = now;
                state.LastProbe = now;
                return;
            }

            _guarded[host] = new GuardState { Since = now, LastProbe = now };
            JacRedLog.Warning(JacRedLogCategories.Host, $"{host} закрыт проверкой Cloudflare, переходим на браузер");
        }

        #endregion

        #region получение страницы

        /// <summary>
        /// Забирает страницу через браузер. Возвращает готовый HTML либо null.
        /// Browser timeout: сначала retry той же сессии, destroy только после
        /// <c>recycleAfterTimeouts</c> подряд (или сразу при явной ошибке session).
        /// <paramref name="referer"/> и <paramref name="extraHeaders"/> уходят в cffetch
        /// и FlareSolverr <c>request.get</c> (nginx Ultradox без поискового Referer → 503).
        /// </summary>
        public static async Task<string> FetchAsync(
            string url,
            string cookie = null,
            string referer = null,
            List<(string name, string val)> extraHeaders = null)
        {
            var conf = Conf;
            if (conf.Url == null || string.IsNullOrWhiteSpace(url))
                return null;

            string host;
            try { host = new Uri(url).Host; }
            catch (UriFormatException) { return null; }

            for (int round = 0; round < 3; round++)
            {
                var (fast, fastHtml) = await TryFastAsync(host, url, cookie, referer, extraHeaders);

                if (fast == FastOutcome.Ok)
                    return fastHtml;

                if (fast == FastOutcome.PageFailed)
                    return null;

                if (fast == FastOutcome.NotAvailable)
                    break;

                if (await ClearanceRenewedAsync(host))
                    break;
            }

            var session = SessionForHost(host);
            await session.Gate.WaitAsync();
            try
            {
                if (!session.Alive && !await CreateSessionAsync(conf, session))
                    return null;

                var (outcome, html, failMessage) = await RequestWithTimeoutRetriesAsync(
                    conf, session, url, cookie, referer, extraHeaders);

                if (outcome == FetchOutcome.Ok)
                {
                    session.ConsecutiveBrowserTimeouts = 0;
                    TouchSession(conf, session);
                    return html;
                }

                if (outcome == FetchOutcome.PageFailed)
                {
                    TouchSession(conf, session);
                    return null;
                }

                bool browserTimeout = IsBrowserTimeoutMessage(failMessage);
                bool sessionBroken = IsSessionBrokenMessage(failMessage);

                if (browserTimeout && !sessionBroken)
                {
                    session.ConsecutiveBrowserTimeouts++;

                    if (session.ConsecutiveBrowserTimeouts < conf.RecycleAfterTimeouts)
                    {
                        JacRedLog.Warning(JacRedLogCategories.Host,
                            $"{host}: FlareSolverr browser timeout ({session.ConsecutiveBrowserTimeouts}/{conf.RecycleAfterTimeouts}) — сессию оставляем, caller ретраит");
                        TouchSession(conf, session);
                        return null;
                    }

                    JacRedLog.Warning(JacRedLogCategories.Host,
                        $"{host}: session recycled after {session.ConsecutiveBrowserTimeouts} browser timeouts");
                }
                else
                {
                    JacRedLog.Warning(JacRedLogCategories.Host,
                        $"{host}: FlareSolverr session recycle — {failMessage}");
                }

                await DestroySessionAsync(conf, session);
                session.ConsecutiveBrowserTimeouts = 0;

                if (!await CreateSessionAsync(conf, session))
                    return null;

                (outcome, html, failMessage) = await RequestWithTimeoutRetriesAsync(
                    conf, session, url, cookie, referer, extraHeaders);

                if (outcome == FetchOutcome.Ok)
                {
                    session.ConsecutiveBrowserTimeouts = 0;
                    JacRedLog.Warning(JacRedLogCategories.Host, $"{host}: session recycled, OK");
                    TouchSession(conf, session);
                    return html;
                }

                if (IsBrowserTimeoutMessage(failMessage))
                    session.ConsecutiveBrowserTimeouts = 1;

                TouchSession(conf, session);
                return null;
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr: {host}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            finally
            {
                ReleaseRenew(host);
                session.Gate.Release();
            }
        }

        enum FastOutcome
        {
            NotAvailable,
            Ok,
            PageFailed,
            ClearanceLost
        }

        static readonly ConcurrentDictionary<string, SemaphoreSlim> _renewGates =
            new(StringComparer.OrdinalIgnoreCase);

        static readonly ConcurrentDictionary<string, SemaphoreSlim> _renewing =
            new(StringComparer.OrdinalIgnoreCase);

        static readonly TimeSpan RenewWait = TimeSpan.FromSeconds(100);

        static async Task<bool> ClearanceRenewedAsync(string host)
        {
            var gate = _renewGates.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));

            if (await gate.WaitAsync(0))
            {
                _renewing[host] = gate;
                return true;
            }

            var deadline = DateTime.UtcNow + RenewWait;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);

                if (CfFetch.For(host) != null)
                    return false;
            }

            return false;
        }

        static void ReleaseRenew(string host)
        {
            if (_renewing.TryRemove(host, out var gate))
                gate.Release();
        }

        static async Task<(FastOutcome outcome, string html)> TryFastAsync(
            string host, string url, string cookie, string referer, List<(string name, string val)> extraHeaders)
        {
            var clearance = CfFetch.For(host);
            if (clearance == null)
                return (FastOutcome.NotAvailable, null);

            var merged = MergeCookies(clearance.Cookies, cookie);
            var requestHeaders = ExtraBrowserHeaders(referer, extraHeaders);

            var (status, body, cfMitigated) = await CfFetch.GetAsync(url, new CfFetch.Clearance
            {
                Cookies = merged,
                UserAgent = clearance.UserAgent,
                At = clearance.At
            }, requestHeaders.Count > 0 ? requestHeaders : null);

            if (status == 0)
                return (FastOutcome.NotAvailable, null);

            if (CfFetch.ClearanceLost(status, body, cfMitigated))
            {
                if (CfFetch.ShouldDropClearance(host))
                {
                    CfFetch.Forget(host);
                    return (FastOutcome.ClearanceLost, null);
                }

                return (FastOutcome.NotAvailable, null);
            }

            if (status == 200 && !string.IsNullOrWhiteSpace(body))
                return (FastOutcome.Ok, body);

            // Old cffetch ignores JSON `headers`; Ultradox nginx 503s without Referer.
            // Do not PageFailed — fall through to FlareSolverr request.get with Referer.
            if (ShouldSkipFastPathForOrigin503(status, body, referer))
                return (FastOutcome.NotAvailable, null);

            return (FastOutcome.PageFailed, null);
        }

        internal static bool ShouldSkipFastPathForOrigin503(int status, string body, string referer)
        {
            if (status != 503 || string.IsNullOrWhiteSpace(referer))
                return false;

            if (string.IsNullOrEmpty(body))
                return true;

            return body.IndexOf("503 Service Temporarily Unavailable", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string MergeCookies(string fromBrowser, string fromCaller)
        {
            if (string.IsNullOrWhiteSpace(fromCaller))
                return fromBrowser;

            var jar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in new[] { fromBrowser, fromCaller })
            {
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                foreach (var part in source.Split(';'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string name = part.Substring(0, eq).Trim();
                    if (name.Length > 0)
                        jar[name] = part.Substring(eq + 1).Trim();
                }
            }

            var sb = new StringBuilder();
            foreach (var pair in jar)
            {
                if (sb.Length > 0)
                    sb.Append("; ");

                sb.Append(pair.Key).Append('=').Append(pair.Value);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Referer + extra GET headers for cffetch / FlareSolverr. Cookie and User-Agent
        /// stay on their own fields so the browser/TLS path is not overwritten.
        /// </summary>
        internal static Dictionary<string, string> ExtraBrowserHeaders(
            string referer, IReadOnlyList<(string name, string val)> extraHeaders)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(referer))
                headers["Referer"] = referer.Trim();

            if (extraHeaders == null)
                return headers;

            foreach (var (name, val) in extraHeaders)
            {
                if (string.IsNullOrWhiteSpace(name) || val == null)
                    continue;

                string key = name.Trim();
                if (key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                    || !IsForwardedBrowserHeader(key))
                {
                    continue;
                }

                headers[key] = val;
            }

            return headers;
        }

        static bool IsForwardedBrowserHeader(string key) =>
            key.Equals("Referer", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Accept", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase);

        static async Task RememberClearance(string url, JObject solution)
        {
            if (!CfFetch.Enabled || solution == null)
                return;

            string host;
            try { host = new Uri(url).Host; }
            catch (UriFormatException) { return; }

            if (CfFetch.For(host) != null)
                return;

            var jar = solution["cookies"] as JArray;
            if (jar == null || jar.Count == 0)
                return;

            var sb = new StringBuilder();
            foreach (var c in jar)
            {
                string name = c.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (sb.Length > 0)
                    sb.Append("; ");

                sb.Append(name).Append('=').Append(c.Value<string>("value"));
            }

            var candidate = new CfFetch.Clearance
            {
                Cookies = sb.ToString(),
                UserAgent = solution.Value<string>("userAgent"),
                At = DateTime.UtcNow
            };

            if (string.IsNullOrWhiteSpace(candidate.Cookies))
                return;

            if (!await CfFetch.ValidateAsync(url, candidate))
            {
                CfFetch.BlockFastPath(host);
                return;
            }

            CfFetch.Remember(host, candidate.Cookies, candidate.UserAgent);
        }

        /// <summary>
        /// Destroy and recreate the Chromium session for this host (stale-shell storms).
        /// No-op when FlareSolverr is disabled.
        /// </summary>
        public static async Task RecycleSession(string host)
        {
            var conf = Conf;
            if (conf.Url == null || string.IsNullOrWhiteSpace(host))
                return;

            var session = SessionForHost(host);
            await session.Gate.WaitAsync();
            try
            {
                JacRedLog.Warning(JacRedLogCategories.Host,
                    $"{host}: FlareSolverr session recycle requested ({session.Name})");
                await DestroySessionAsync(conf, session);
                session.ConsecutiveBrowserTimeouts = 0;
                await CreateSessionAsync(conf, session);
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr recycle {host}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                session.Gate.Release();
            }
        }

        static void TouchSession(FlareSolverrSettingsView conf, BrowserSession session)
        {
            session.LastUse = DateTime.UtcNow;
            ArmIdleTimer(conf);
        }

        /// <summary>Same-session retries on browser timeout before escalating.</summary>
        static async Task<(FetchOutcome outcome, string html, string failMessage)> RequestWithTimeoutRetriesAsync(
            FlareSolverrSettingsView conf, BrowserSession session, string url, string cookie,
            string referer, List<(string name, string val)> extraHeaders)
        {
            int attempts = 1 + conf.BrowserTimeoutRetries;
            FetchOutcome outcome = FetchOutcome.BrowserFailed;
            string html = null;
            string failMessage = null;

            for (int i = 0; i < attempts; i++)
            {
                if (i > 0)
                    await Task.Delay(1500);

                (outcome, html, failMessage) = await RequestAsync(conf, session, url, cookie, referer, extraHeaders);

                if (outcome != FetchOutcome.BrowserFailed)
                    return (outcome, html, failMessage);

                if (!IsBrowserTimeoutMessage(failMessage) || IsSessionBrokenMessage(failMessage))
                    return (outcome, html, failMessage);

                if (i + 1 < attempts)
                    JacRedLog.Warning(JacRedLogCategories.Host,
                        $"FlareSolverr browser timeout — same-session retry {i + 1}/{conf.BrowserTimeoutRetries}");
            }

            return (outcome, html, failMessage);
        }

        enum FetchOutcome
        {
            Ok,
            PageFailed,
            BrowserFailed
        }

        static async Task<(FetchOutcome outcome, string html, string failMessage)> RequestAsync(
            FlareSolverrSettingsView conf, BrowserSession session, string url, string cookie,
            string referer, List<(string name, string val)> extraHeaders)
        {
            var payload = new Dictionary<string, object>
            {
                ["cmd"] = "request.get",
                ["session"] = session.Name,
                ["url"] = url,
                ["maxTimeout"] = conf.MaxTimeoutMs
            };

            var jar = ParseCookies(cookie);
            if (jar.Count > 0)
                payload["cookies"] = jar;

            var headers = ExtraBrowserHeaders(referer, extraHeaders);
            if (headers.Count > 0)
                payload["headers"] = headers;

            // Proxy только через PROXY_* у контейнера FlareSolverr — в body не шлём
            // (при session FlareSolverr всё равно игнорирует request proxy).

            var root = await CallAsync(conf, payload, conf.MaxTimeoutMs + 30000);

            if (root == null)
            {
                session.Alive = false;
                return (FetchOutcome.BrowserFailed, null, "empty response / unreachable");
            }

            if (!string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase))
            {
                string message = root.Value<string>("message") ?? "";
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr отказал: {message}");
                return (FetchOutcome.BrowserFailed, null, message);
            }

            var solution = root.Value<JObject>("solution");
            int status = solution?.Value<int?>("status") ?? 0;
            string html = solution?.Value<string>("response");

            if (status != 200 || string.IsNullOrWhiteSpace(html))
                return (FetchOutcome.PageFailed, null, $"http {status}");

            // FS иногда отдаёт status=ok со страницей interstitial — не считаем успехом.
            if (IsChallengeBody(html))
                return (FetchOutcome.PageFailed, null, "challenge html in solution");

            // Origin nginx 503 behind CF: FS still reports solution.status=200.
            if (html.Length < 2000
                && html.Contains("503 Service Temporarily Unavailable", StringComparison.OrdinalIgnoreCase))
                return (FetchOutcome.PageFailed, null, "origin 503");

            await RememberClearance(url, solution);
            return (FetchOutcome.Ok, html, null);
        }

        static bool IsBrowserTimeoutMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            return message.IndexOf("Read timed out", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("HTTPConnectionPool", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("Timeout after", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsSessionBrokenMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            // "Session not found" / "Session timeout" и т.п. — не путать с request timeout.
            return message.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0
                   && !IsBrowserTimeoutMessage(message);
        }

        static List<Dictionary<string, string>> ParseCookies(string cookie)
        {
            var list = new List<Dictionary<string, string>>();
            if (string.IsNullOrWhiteSpace(cookie))
                return list;

            foreach (var part in cookie.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;

                string name = part.Substring(0, eq).Trim();
                string value = part.Substring(eq + 1).Trim();

                if (name.Length > 0)
                    list.Add(new Dictionary<string, string> { ["name"] = name, ["value"] = value });
            }

            return list;
        }

        #endregion

        #region сессия

        static async Task<bool> CreateSessionAsync(FlareSolverrSettingsView conf, BrowserSession session)
        {
            var root = await CallAsync(conf, new Dictionary<string, object>
            {
                ["cmd"] = "sessions.create",
                ["session"] = session.Name
            }, conf.MaxTimeoutMs + 30000);

            bool ok = root != null &&
                      (string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase)
                       || (root.Value<string>("message") ?? "").IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0);

            session.Alive = ok;

            if (ok)
                JacRedLog.Warning(JacRedLogCategories.Host, $"FlareSolverr: сессия {session.Name} создана");
            else
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr: сессию {session.Name} создать не удалось: {root?.Value<string>("message")}");

            return ok;
        }

        static async Task DestroySessionAsync(FlareSolverrSettingsView conf, BrowserSession session)
        {
            await CallAsync(conf, new Dictionary<string, object>
            {
                ["cmd"] = "sessions.destroy",
                ["session"] = session.Name
            }, 60000);

            session.Alive = false;
        }

        static void ArmIdleTimer(FlareSolverrSettingsView conf)
        {
            if (conf.SessionIdleMinutes <= 0)
                return;

            _idleTimer ??= new Timer(_ => CloseIfIdle(), null, Timeout.Infinite, Timeout.Infinite);

            var period = TimeSpan.FromMinutes(1);
            _idleTimer.Change(period, period);
        }

        static void CloseIfIdle()
        {
            var settings = AppInit.conf?.flaresolverr;
            if (settings == null || !settings.enable || string.IsNullOrWhiteSpace(settings.url)
                || settings.sessionIdleMinutes <= 0)
                return;

            foreach (var session in _sessions.Values)
            {
                if (!session.Alive)
                    continue;

                if (DateTime.UtcNow < session.LastUse.AddMinutes(settings.sessionIdleMinutes))
                    continue;

                if (!session.Gate.Wait(0))
                    continue;

                try
                {
                    var sessionConf = string.IsNullOrWhiteSpace(session.SolverUrl)
                        ? default
                        : new FlareSolverrSettingsView(settings, session.SolverUrl);
                    if (sessionConf.Url == null)
                        continue;

                    CallAsync(sessionConf, new Dictionary<string, object>
                    {
                        ["cmd"] = "sessions.destroy",
                        ["session"] = session.Name
                    }, 60000).GetAwaiter().GetResult();

                    session.Alive = false;
                    JacRedLog.Warning(JacRedLogCategories.Host,
                        $"FlareSolverr: сессия {session.Name} закрыта по простою, память освобождена");
                }
                catch (Exception ex)
                {
                    JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr: не удалось закрыть сессию {session.Name}: {ex.Message}");
                }
                finally
                {
                    session.Gate.Release();
                }
            }
        }

        #endregion

        static async Task<JObject> CallAsync(FlareSolverrSettingsView conf, Dictionary<string, object> payload, int timeoutMs)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
                using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(conf.Url, content);

                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr недоступен: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}
