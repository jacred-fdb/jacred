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

namespace JacRed.Infrastructure.Middleware
{
    /// <summary>
    /// Middleware: access control for JacRed API and admin paths.
    ///
    /// Two client notions (see Startup UseForwardedHeaders + CaptureOriginalRemoteIp):
    /// - <b>Client IP</b> — RemoteIpAddress after X-Forwarded-For (who uses the service).
    /// - <b>Peer IP</b> — direct TCP connection to Kestrel (cloudflared/nginx on same host → loopback/private).
    ///
    /// | Path group | LAN client | Same-host proxy (CF Tunnel / local nginx) | Remote proxy / internet |
    /// |------------|------------|-------------------------------------------|-------------------------|
    /// | /dev/, /cron/, /jsondb | ✓ | devkey if set, else ✗ | devkey if set, else ✗ |
    /// | /api/v1.0/config | ✓ | ✓ без devkey; devkey если задан | devkey |
    /// | Search etc. | apikey if configured | apikey if configured | apikey if configured |
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

        /// <summary>Configuration API (/api/v1.0/config/*).</summary>
        private static bool IsConfigApiPath(string path)
            => !string.IsNullOrEmpty(path)
                && path.StartsWith("/api/v1.0/config", StringComparison.OrdinalIgnoreCase);

        /// <summary>Admin/dev paths: /dev/, /cron/, /jsondb — never open to remote clients without devkey.</summary>
        private static bool IsDevOnlyPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/jsondb", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/jsondb/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>All paths with elevated restrictions (dev + config API).</summary>
        private static bool IsRestrictedAdminPath(string path)
            => IsDevOnlyPath(path) || IsConfigApiPath(path);

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

        /// <summary>Whether to add Access-Control-Allow-Private-Network (local network or non-restricted path).</summary>
        private static bool ShouldSetPrivateNetworkHeader(bool trustedContext, string path)
        {
            return trustedContext || !IsRestrictedAdminPath(path);
        }

        /// <summary>HttpContext.Items key for TCP remote IP before ForwardedHeaders.</summary>
        public const string OriginalRemoteIpItemKey = "JacRed.OriginalRemoteIp";

        /// <summary>Store direct connection IP before UseForwardedHeaders overwrites RemoteIpAddress.</summary>
        public static void CaptureOriginalRemoteIp(HttpContext httpContext)
        {
            httpContext.Items[OriginalRemoteIpItemKey] = httpContext.Connection.RemoteIpAddress;
        }

        /// <summary>Direct TCP peer is loopback or RFC1918 (cloudflared/nginx on the same host or Docker network).</summary>
        private static bool IsViaLocalPeer(HttpContext httpContext)
        {
            if (!httpContext.Items.TryGetValue(OriginalRemoteIpItemKey, out var value) || value is not IPAddress peer)
                return false;
            return IsLocalOrPrivate(peer);
        }

        /// <summary>Client IP (after forwarded headers) is loopback or RFC1918 — true LAN/localhost user.</summary>
        private static bool IsDirectLocalClient(HttpContext httpContext)
            => IsLocalOrPrivate(httpContext.Connection.RemoteIpAddress);

        /// <summary>LAN client or request arrived through a local reverse proxy / Cloudflare Tunnel on this host.</summary>
        private static bool IsTrustedContext(HttpContext httpContext)
            => IsDirectLocalClient(httpContext) || IsViaLocalPeer(httpContext);

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

        /// <summary>
        /// /dev/, /cron/, /jsondb: only a true LAN/localhost client, or remote with valid devkey.
        /// Same-host Cloudflare Tunnel / nginx does NOT grant access without devkey.
        /// </summary>
        private static bool IsDevEndpointAccessAllowed(HttpContext httpContext)
        {
            if (IsDirectLocalClient(httpContext))
                return true;

            if (!string.IsNullOrEmpty(AppInit.conf?.devkey))
                return DevKeyMatches(httpContext);

            return false;
        }

        /// <summary>
        /// /api/v1.0/config: LAN; same-host proxy (CF Tunnel / local nginx) без devkey;
        /// из интернета или через туннель при заданном devkey — с ключом.
        /// </summary>
        private static bool IsConfigApiAccessAllowed(HttpContext httpContext)
        {
            if (IsDirectLocalClient(httpContext))
                return true;

            if (IsViaLocalPeer(httpContext))
            {
                if (string.IsNullOrEmpty(AppInit.conf?.devkey))
                    return true;
                return DevKeyMatches(httpContext);
            }

            if (!string.IsNullOrEmpty(AppInit.conf?.devkey) && DevKeyMatches(httpContext))
                return true;

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

        /// <summary>Sets standard security headers (CSP skipped for Swagger UI).</summary>
        public static void ApplySecurityHeaders(HttpContext httpContext)
        {
            var headers = httpContext.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-Frame-Options"] = "SAMEORIGIN";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            var path = httpContext.Request.Path.Value ?? "";
            if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/openapi.yaml", StringComparison.OrdinalIgnoreCase))
                return;

            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "frame-ancestors 'self'; " +
                "object-src 'none'; " +
                "script-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "font-src 'self'; " +
                "img-src 'self' data:; " +
                "connect-src 'self' https: http:; " +
                "manifest-src 'self'; " +
                "worker-src 'self'";
        }

        /// <summary>Public web/PWA paths that must stay reachable without apikey (also served from wwwroot when web=true).</summary>
        private static bool IsPublicWebPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.Equals("/opensearch.xml", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/manifest.json", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/sw.js", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/img/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/vendor/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/fonts/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathWhitelisted(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.Equals("/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/stats", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/stats/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/settings", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/settings/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/health", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/version", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/lastupdatedb", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/openapi.yaml", StringComparison.OrdinalIgnoreCase)
                || IsPublicWebPath(path)
                || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                || PathWhitelistRegex().IsMatch(path);
        }

        static int DenyStatus(bool keyConfigured, string method)
            => method == "OPTIONS" ? 204 : (keyConfigured ? 401 : 403);

        /// <summary>Handles request: IP check, devkey, apikey, CORS, cron logging.</summary>
        public async Task Invoke(HttpContext httpContext)
        {
            ApplySecurityHeaders(httpContext);

            string path = httpContext.Request.Path.Value ?? "";
            bool trustedContext = IsTrustedContext(httpContext);
            bool devkeyConfigured = !string.IsNullOrEmpty(AppInit.conf?.devkey);

            if (IsDevOnlyPath(path) && !IsDevEndpointAccessAllowed(httpContext))
            {
                SetPrivateNetworkHeader(httpContext);
                httpContext.Response.StatusCode = DenyStatus(devkeyConfigured, httpContext.Request.Method);
                return;
            }

            if (IsConfigApiPath(path) && !IsConfigApiAccessAllowed(httpContext))
            {
                SetPrivateNetworkHeader(httpContext);
                httpContext.Response.StatusCode = DenyStatus(devkeyConfigured, httpContext.Request.Method);
                return;
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
                    if (ShouldSetPrivateNetworkHeader(trustedContext, path))
                        SetPrivateNetworkHeader(httpContext);
                    httpContext.Response.StatusCode = DenyStatus(keyConfigured: true, httpContext.Request.Method);
                    return;
                }
            }

            // CORS: Allow-Private-Network (Chrome PNA) when request passes checks
            if (ShouldSetPrivateNetworkHeader(trustedContext, path))
                SetPrivateNetworkHeader(httpContext);

            bool isCron = path.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase);
            var cronStopwatch = isCron ? Stopwatch.StartNew() : null;

            await _next(httpContext);

            if (isCron && cronStopwatch != null)
            {
                cronStopwatch.Stop();
                var label = path.Substring(6);
                var elapsed = cronStopwatch.ElapsedMilliseconds >= 1000
                    ? $"{cronStopwatch.ElapsedMilliseconds / 1000.0:F1}s"
                    : $"{cronStopwatch.ElapsedMilliseconds}ms";
                var status = httpContext.Response.StatusCode;
                var ts = DateTime.Now.ToString("HH:mm:ss");
                var fail = status >= 400 ? " FAIL" : "";
                Console.WriteLine($"cron: [{ts}] {label} {elapsed} {status}{fail}");
            }
        }
    }
}
