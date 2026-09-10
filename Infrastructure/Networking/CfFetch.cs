using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JacRed.Infrastructure.Networking
{
    /// <summary>
    /// Cookie <c>cf_clearance</c> от FlareSolverr + TLS Chrome через localhost cffetch.
    /// Протокол как у JacBlack: merge jar, ValidateAsync, BlockFastPath,
    /// голый 403 не отзыв, три cf-mitigated за минуту.
    /// </summary>
    public static class CfFetch
    {
        public sealed class Clearance
        {
            public string Cookies { get; init; }
            public string UserAgent { get; init; }
            public DateTime At { get; init; }
        }

        static readonly ConcurrentDictionary<string, Clearance> _clearance =
            new(StringComparer.OrdinalIgnoreCase);

        static SemaphoreSlim _gate;
        static int _gateSize;
        static DateTime _lastDownLog = DateTime.MinValue;

        static Models.AppConf.CfFetchSettings Conf
        {
            get
            {
                var c = AppInit.conf?.cffetch;
                return c == null || !c.enable || string.IsNullOrWhiteSpace(c.url) ? null : c;
            }
        }

        public static bool Enabled => Conf != null;

        #region cookie от браузера

        public static void Remember(string host, string cookies, string userAgent)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(cookies))
                return;

            _clearance.TryGetValue(host, out var had);
            string merged = Merge(had?.Cookies, cookies);

            _clearance[host] = new Clearance
            {
                Cookies = merged,
                UserAgent = string.IsNullOrWhiteSpace(userAgent) ? had?.UserAgent : userAgent,
                At = DateTime.UtcNow
            };

            if (had == null)
                JacRedLog.Warning(JacRedLogCategories.Host, $"{host}: cookie от браузера получена, дальше идём быстрым путём");
        }

        static string Merge(string old, string fresh)
        {
            if (string.IsNullOrWhiteSpace(old))
                return fresh;

            var jar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in new[] { old, fresh })
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

        static readonly ConcurrentDictionary<string, DateTime> _blocked =
            new(StringComparer.OrdinalIgnoreCase);

        const int BlockedMinutes = 30;

        public static void BlockFastPath(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            var until = DateTime.UtcNow.AddMinutes(BlockedMinutes);
            bool first = !_blocked.ContainsKey(host) || _blocked[host] < DateTime.UtcNow;
            _blocked[host] = until;

            if (first)
                JacRedLog.Warning(JacRedLogCategories.Host,
                    $"{host}: cookie от браузера помощнику не годится, {BlockedMinutes} мин ходим браузером");
        }

        public static bool FastPathBlocked(string host) =>
            !string.IsNullOrWhiteSpace(host)
            && _blocked.TryGetValue(host, out var until)
            && DateTime.UtcNow < until;

        public static Clearance For(string host)
        {
            var conf = Conf;
            if (conf == null || string.IsNullOrWhiteSpace(host))
                return null;

            if (FastPathBlocked(host))
                return null;

            if (!_clearance.TryGetValue(host, out var c))
                return null;

            if (conf.clearanceMinutes > 0 && DateTime.UtcNow > c.At.AddMinutes(conf.clearanceMinutes))
                return null;

            return c;
        }

        public static void Forget(string host)
        {
            if (!string.IsNullOrWhiteSpace(host) && _clearance.TryRemove(host, out _))
                JacRedLog.Warning(JacRedLogCategories.Host, $"{host}: cookie больше не проходит, возвращаемся к браузеру");
        }

        internal static void Reset()
        {
            _clearance.Clear();
            _mitigated.Clear();
            _blocked.Clear();
        }

        public static async Task<bool> ValidateAsync(string url, Clearance candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(url))
                return false;

            var (status, body, mitigated) = await GetAsync(url, candidate);

            if (status == 0)
                return true;

            return !ClearanceLost(status, body, mitigated);
        }

        #endregion

        #region терпимость к одиночным отказам

        sealed class MitigationRun
        {
            public DateTime Since;
            public int Count;
        }

        static readonly ConcurrentDictionary<string, MitigationRun> _mitigated =
            new(StringComparer.OrdinalIgnoreCase);

        const int MitigationsToDrop = 3;
        static readonly TimeSpan MitigationWindow = TimeSpan.FromSeconds(60);

        public static bool ShouldDropClearance(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;

            var run = _mitigated.GetOrAdd(host, _ => new MitigationRun { Since = DateTime.UtcNow });

            lock (run)
            {
                var now = DateTime.UtcNow;

                if (now - run.Since > MitigationWindow)
                {
                    run.Since = now;
                    run.Count = 0;
                }

                run.Count++;

                if (run.Count < MitigationsToDrop)
                    return false;

                run.Since = now;
                run.Count = 0;
                return true;
            }
        }

        #endregion

        public static bool ClearanceLost(int status, string body, bool cfMitigated = false)
        {
            if (cfMitigated)
                return true;

            if (CloudflareClearance.IsChallengeBody(body))
                return true;

            return false;
        }

        public static async Task<(int status, string body, bool cfMitigated)> GetAsync(
            string url, Clearance clearance, IReadOnlyDictionary<string, string> extraHeaders = null)
        {
            var conf = Conf;
            if (conf == null || clearance == null || string.IsNullOrWhiteSpace(url))
                return (0, null, false);

            var gate = Gate(conf);
            await gate.WaitAsync();
            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["url"] = url,
                    ["cookies"] = clearance.Cookies,
                    ["userAgent"] = clearance.UserAgent ?? string.Empty,
                    ["impersonate"] = conf.impersonate,
                    ["timeout"] = conf.timeoutSeconds
                };

                if (!string.IsNullOrWhiteSpace(conf.proxy))
                    payload["proxy"] = conf.proxy;

                if (extraHeaders != null && extraHeaders.Count > 0)
                    payload["headers"] = extraHeaders;

                using var client = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(conf.timeoutSeconds + 10)
                };

                using var content = new System.Net.Http.StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                using var response = await client.PostAsync(conf.url, content);

                var root = JObject.Parse(await response.Content.ReadAsStringAsync());

                int status = root.Value<int?>("status") ?? 0;
                string body = root.Value<string>("body");

                if (status == 0)
                {
                    string error = root.Value<string>("error");
                    if (!string.IsNullOrWhiteSpace(error))
                        JacRedLog.Warning(JacRedLogCategories.Host, $"cffetch: {url}: {error}");
                }

                bool mitigated = root.Value<bool?>("cfMitigated") ?? false;
                return (status, body, mitigated);
            }
            catch (Exception ex)
            {
                var now = DateTime.UtcNow;
                if (now - _lastDownLog > TimeSpan.FromMinutes(1))
                {
                    _lastDownLog = now;
                    JacRedLog.Warning(JacRedLogCategories.Host,
                        $"cffetch недоступен ({ex.GetType().Name}), уходим на браузер");
                }

                return (0, null, false);
            }
            finally
            {
                gate.Release();
            }
        }

        static SemaphoreSlim Gate(Models.AppConf.CfFetchSettings conf)
        {
            int size = conf.maxConcurrent > 0 ? conf.maxConcurrent : 1;

            if (_gate == null || _gateSize != size)
            {
                _gate = new SemaphoreSlim(size, size);
                _gateSize = size;
            }

            return _gate;
        }
    }
}
