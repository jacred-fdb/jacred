using Microsoft.AspNetCore.Http;
using System;
using JacRed;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JacRed.Engine.Middlewares
{
    /// <summary>
    /// Middleware: IP restriction (local/private network), devkey for /cron/, /jsondb, /dev/,
    /// apikey for search and stats, CORS Access-Control-Allow-Private-Network, cron logging.
    /// </summary>
    public partial class ModHeaders
    {
        private readonly RequestDelegate _next;

        [GeneratedRegex("(\\?|&)apikey=([^&]+)")]
        private static partial Regex ApiKeyQueryRegex();

        [GeneratedRegex("(\\?|&)devkey=([^&]+)")]
        private static partial Regex DevKeyQueryRegex();

        /// <summary>Paths starting with /api/v1.0/conf or /sync/ (whitelisted for apikey check).</summary>
        [GeneratedRegex("^/(api/v1\\.0/conf|sync/)")]
        private static partial Regex PathWhitelistRegex();

        /// <summary>Paths restricted to local/private network (by client IP).</summary>
        private static bool IsLocalOnlyPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/jsondb", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/jsondb/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase);
        }

        public ModHeaders(RequestDelegate next)
        {
            _next = next;
        }

        /// <summary>Constant-time string comparison (timing-attack resistant).
        /// Keys should be ASCII-only; UTF-8 can produce multiple byte sequences for the same logical string.</summary>
        private static bool SecureEquals(string a, string b)
        {
            if (a == null || b == null) return a == b;
            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);
            return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }

        /// <summary>URL-decodes query value; returns raw string on malformed percent-encoding.</summary>
        private static string DecodeQueryValue(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            try { return Uri.UnescapeDataString(raw); }
            catch (UriFormatException) { return raw; }
        }

        /// <summary>Sets CORS Access-Control-Allow-Private-Network for Chrome PNA.</summary>
        private static void SetPrivateNetworkHeader(HttpContext ctx)
        {
            ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }

        /// <summary>Whether to add Access-Control-Allow-Private-Network (local network or non-local-only path).</summary>
        private static bool ShouldSetPrivateNetworkHeader(bool fromLocalNetwork, string path)
        {
            return fromLocalNetwork || !IsLocalOnlyPath(path);
        }

        private static bool IsLocalOrPrivate(IPAddress remoteIp)
        {
            if (remoteIp == null) return false;
            if (remoteIp.IsIPv4MappedToIPv6)
                return IsLocalOrPrivate(remoteIp.MapToIPv4());
            var bytes = remoteIp.GetAddressBytes();
            if (remoteIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (bytes[0] == 127) return true;                                        // 127.0.0.0/8
                if (bytes[0] == 10) return true;                                          // 10.0.0.0/8
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;    // 172.16.0.0/12
                if (bytes[0] == 192 && bytes[1] == 168) return true;                      // 192.168.0.0/16
                return false;
            }
            if (remoteIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (IPAddress.IPv6Loopback.Equals(remoteIp)) return true;
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true; // fe80::/10 link-local
                if ((bytes[0] & 0xfe) == 0xfc) return true;                      // fc00::/7 unique local
                return false;
            }
            return false;
        }

        private static bool DevKeyMatches(HttpContext httpContext)
        {
            var key = AppInit.conf?.devkey;
            if (string.IsNullOrEmpty(key)) return true;
            if (httpContext.Request.Headers.TryGetValue("X-Dev-Key", out var h) && !string.IsNullOrEmpty(h))
                return SecureEquals(h.FirstOrDefault() ?? "", key);
            var match = DevKeyQueryRegex().Match(httpContext.Request.QueryString.Value ?? "");
            if (match.Success) return SecureEquals(DecodeQueryValue(match.Groups[2].Value), key);
            return false;
        }

        /// <summary>Extracts apikey from query (?apikey=), X-Api-Key header, or Authorization: Bearer.</summary>
        private static string GetApiKeyFromRequest(HttpContext httpContext)
        {
            var match = ApiKeyQueryRegex().Match(httpContext.Request.QueryString.Value ?? "");
            if (match.Success) return DecodeQueryValue(match.Groups[2].Value);
            if (httpContext.Request.Headers.TryGetValue("X-Api-Key", out var h) && !string.IsNullOrEmpty(h))
                return (h.FirstOrDefault() ?? "").Trim();
            if (httpContext.Request.Headers.TryGetValue("Authorization", out var auth))
            {
                var s = auth.ToString();
                if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return s.Substring(7).Trim();
            }
            return null;
        }

        private static bool IsPathWhitelisted(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.Equals("/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/stats", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/stats/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/health", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/version", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/lastupdatedb", StringComparison.OrdinalIgnoreCase)
                || PathWhitelistRegex().IsMatch(path);
        }

        /// <summary>Handles request: IP check, devkey, apikey, CORS, cron logging.</summary>
        public async Task Invoke(HttpContext httpContext)
        {
            bool fromLocalNetwork = IsLocalOrPrivate(httpContext.Connection.RemoteIpAddress);
            string path = httpContext.Request.Path.Value ?? "";

            if (!fromLocalNetwork)
            {
                // /cron/, /jsondb, /dev/ — local network only
                if (IsLocalOnlyPath(path))
                {
                    // OPTIONS: 204 for CORS preflight. UseCors runs before UseModHeaders, so CORS headers are applied to the response.
                    httpContext.Response.StatusCode = httpContext.Request.Method == "OPTIONS" ? 204 : 403;
                    return;
                }
            }
            else
            {
                // Behind tunnel/proxy: require devkey for /dev/, /cron/, /jsondb
                if (IsLocalOnlyPath(path) && !string.IsNullOrEmpty(AppInit.conf?.devkey) && !DevKeyMatches(httpContext))
                {
                    SetPrivateNetworkHeader(httpContext);
                    httpContext.Response.StatusCode = httpContext.Request.Method == "OPTIONS" ? 204 : 401;
                    return;
                }
            }

            // API key required for search/stats (local and external)
            if (!string.IsNullOrEmpty(AppInit.conf?.apikey))
            {
                if (IsPathWhitelisted(path))
                {
                    await _next(httpContext);
                    return;
                }

                var providedKey = GetApiKeyFromRequest(httpContext);
                if (string.IsNullOrEmpty(providedKey) || !SecureEquals(providedKey, AppInit.conf?.apikey))
                {
                    if (ShouldSetPrivateNetworkHeader(fromLocalNetwork, path))
                        SetPrivateNetworkHeader(httpContext);
                    httpContext.Response.StatusCode = httpContext.Request.Method == "OPTIONS" ? 204 : 401;
                    return;
                }
            }

            // CORS: Allow-Private-Network (Chrome PNA) when request passes checks
            if (ShouldSetPrivateNetworkHeader(fromLocalNetwork, path))
                SetPrivateNetworkHeader(httpContext);

            bool isCron = path.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase);
            var cronStopwatch = isCron ? Stopwatch.StartNew() : null;

            await _next(httpContext);

            if (isCron && cronStopwatch != null)
            {
                cronStopwatch.Stop();
                var label = path.Substring(6);
                var elapsed = cronStopwatch.ElapsedMilliseconds >= 1000
                    ? $"{cronStopwatch.Elapsed.TotalSeconds:F1}s"
                    : $"{cronStopwatch.ElapsedMilliseconds}ms";
                var status = httpContext.Response.StatusCode;
                var ts = DateTime.Now.ToString("HH:mm:ss");
                var fail = status >= 400 ? " FAIL" : "";
                Console.WriteLine($"cron: [{ts}] {label} {elapsed} {status}{fail}");
            }
        }
    }
}
