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
    /// Ходит на хосты, закрытые проверкой Cloudflare, через FlareSolverr —
    /// безголовый браузер, стоящий рядом в compose.
    ///
    /// Cookie `cf_clearance` нельзя переиспользовать в обычном .NET HttpClient:
    /// Cloudflare сверяет TLS-отпечаток. Поэтому guarded-хосты обслуживает
    /// браузер целиком, в одной постоянной сессии (первая ~80 с, далее 2–3 с).
    /// </summary>
    public static class CloudflareClearance
    {
        const string SessionName = "jacred";

        sealed class GuardState
        {
            public DateTime Since;

            /// <summary>Когда последний раз давали дешёвому пути шанс.</summary>
            public DateTime LastProbe;
        }

        static readonly ConcurrentDictionary<string, GuardState> _guarded = new(StringComparer.OrdinalIgnoreCase);

        static readonly SemaphoreSlim _gate = new(1, 1);

        static bool _sessionAlive;
        static DateTime _lastUse = DateTime.MinValue;
        static Timer _idleTimer;
        static int _consecutiveBrowserTimeouts;

        static FlareSolverrSettingsView Conf
        {
            get
            {
                var c = AppInit.conf?.flaresolverr;

                return c == null || !c.enable || string.IsNullOrWhiteSpace(c.url)
                    ? default
                    : new FlareSolverrSettingsView(c);
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

            public FlareSolverrSettingsView(Models.AppConf.FlareSolverrSettings c)
            {
                Url = c.url;
                MaxTimeoutMs = c.maxTimeoutMs;
                SessionIdleMinutes = c.sessionIdleMinutes;
                BrowserTimeoutRetries = Math.Max(0, c.browserTimeoutRetries);
                RecycleAfterTimeouts = Math.Max(1, c.recycleAfterTimeouts);
                GuardedHours = c.guardedHours;
                RecheckMinutes = c.recheckMinutes;
            }
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
        /// </summary>
        public static async Task<string> FetchAsync(string url, string cookie = null)
        {
            var conf = Conf;
            if (conf.Url == null || string.IsNullOrWhiteSpace(url))
                return null;

            string host;
            try { host = new Uri(url).Host; }
            catch (UriFormatException) { return null; }

            await _gate.WaitAsync();
            try
            {
                if (!_sessionAlive && !await CreateSessionAsync(conf))
                    return null;

                var (outcome, html, failMessage) = await RequestWithTimeoutRetriesAsync(conf, url, cookie);

                if (outcome == FetchOutcome.Ok)
                {
                    _consecutiveBrowserTimeouts = 0;
                    TouchSession(conf);
                    return html;
                }

                if (outcome == FetchOutcome.PageFailed)
                {
                    TouchSession(conf);
                    return null;
                }

                bool browserTimeout = IsBrowserTimeoutMessage(failMessage);
                bool sessionBroken = IsSessionBrokenMessage(failMessage);

                if (browserTimeout && !sessionBroken)
                {
                    _consecutiveBrowserTimeouts++;

                    if (_consecutiveBrowserTimeouts < conf.RecycleAfterTimeouts)
                    {
                        JacRedLog.Warning(JacRedLogCategories.Host,
                            $"{host}: FlareSolverr browser timeout ({_consecutiveBrowserTimeouts}/{conf.RecycleAfterTimeouts}) — сессию оставляем, caller ретраит");
                        TouchSession(conf);
                        return null;
                    }

                    JacRedLog.Warning(JacRedLogCategories.Host,
                        $"{host}: session recycled after {_consecutiveBrowserTimeouts} browser timeouts");
                }
                else
                {
                    JacRedLog.Warning(JacRedLogCategories.Host,
                        $"{host}: FlareSolverr session recycle — {failMessage}");
                }

                await DestroySessionAsync(conf);
                _consecutiveBrowserTimeouts = 0;

                if (!await CreateSessionAsync(conf))
                    return null;

                (outcome, html, failMessage) = await RequestWithTimeoutRetriesAsync(conf, url, cookie);

                if (outcome == FetchOutcome.Ok)
                {
                    _consecutiveBrowserTimeouts = 0;
                    JacRedLog.Warning(JacRedLogCategories.Host, $"{host}: session recycled, OK");
                    TouchSession(conf);
                    return html;
                }

                if (IsBrowserTimeoutMessage(failMessage))
                    _consecutiveBrowserTimeouts = 1;

                TouchSession(conf);
                return null;
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr: {host}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        static void TouchSession(FlareSolverrSettingsView conf)
        {
            _lastUse = DateTime.UtcNow;
            ArmIdleTimer(conf);
        }

        /// <summary>Same-session retries on browser timeout before escalating.</summary>
        static async Task<(FetchOutcome outcome, string html, string failMessage)> RequestWithTimeoutRetriesAsync(
            FlareSolverrSettingsView conf, string url, string cookie)
        {
            int attempts = 1 + conf.BrowserTimeoutRetries;
            FetchOutcome outcome = FetchOutcome.BrowserFailed;
            string html = null;
            string failMessage = null;

            for (int i = 0; i < attempts; i++)
            {
                if (i > 0)
                    await Task.Delay(1500);

                (outcome, html, failMessage) = await RequestAsync(conf, url, cookie);

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

        static async Task<(FetchOutcome outcome, string html, string failMessage)> RequestAsync(FlareSolverrSettingsView conf, string url, string cookie)
        {
            var payload = new Dictionary<string, object>
            {
                ["cmd"] = "request.get",
                ["session"] = SessionName,
                ["url"] = url,
                ["maxTimeout"] = conf.MaxTimeoutMs
            };

            var jar = ParseCookies(cookie);
            if (jar.Count > 0)
                payload["cookies"] = jar;

            // Proxy только через PROXY_* у контейнера FlareSolverr — в body не шлём
            // (при session FlareSolverr всё равно игнорирует request proxy).

            var root = await CallAsync(conf, payload, conf.MaxTimeoutMs + 30000);

            if (root == null)
                return (FetchOutcome.BrowserFailed, null, "empty response / unreachable");

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

        static async Task<bool> CreateSessionAsync(FlareSolverrSettingsView conf)
        {
            var root = await CallAsync(conf, new Dictionary<string, object>
            {
                ["cmd"] = "sessions.create",
                ["session"] = SessionName
            }, conf.MaxTimeoutMs + 30000);

            bool ok = root != null &&
                      (string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase)
                       || (root.Value<string>("message") ?? "").IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0);

            _sessionAlive = ok;

            if (ok)
                JacRedLog.Warning(JacRedLogCategories.Host, "FlareSolverr: сессия браузера создана");
            else
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr: сессию создать не удалось: {root?.Value<string>("message")}");

            return ok;
        }

        static async Task DestroySessionAsync(FlareSolverrSettingsView conf)
        {
            await CallAsync(conf, new Dictionary<string, object>
            {
                ["cmd"] = "sessions.destroy",
                ["session"] = SessionName
            }, 60000);

            _sessionAlive = false;
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
            var conf = Conf;
            if (conf.Url == null || !_sessionAlive || conf.SessionIdleMinutes <= 0)
                return;

            if (DateTime.UtcNow < _lastUse.AddMinutes(conf.SessionIdleMinutes))
                return;

            if (!_gate.Wait(0))
                return;

            try
            {
                CallAsync(conf, new Dictionary<string, object>
                {
                    ["cmd"] = "sessions.destroy",
                    ["session"] = SessionName
                }, 60000).GetAwaiter().GetResult();

                _sessionAlive = false;
                _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                JacRedLog.Warning(JacRedLogCategories.Host, "FlareSolverr: сессия закрыта по простою, память освобождена");
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr: не удалось закрыть сессию: {ex.Message}");
            }
            finally
            {
                _gate.Release();
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
                _sessionAlive = false;
                return null;
            }
        }
    }
}
