using Microsoft.AspNetCore.Http;
using System;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JacRed.Engine.Middlewares
{
    public class ModHeaders
    {
        private readonly RequestDelegate _next;

        /// <summary>Пути, доступные только из локальной/приватной сети (по IP клиента).</summary>
        private static bool IsLocalOnlyPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/jsondb", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase);
        }

        public ModHeaders(RequestDelegate next)
        {
            _next = next;
        }

        private static bool IsLocalOrPrivate(IPAddress remoteIp)
        {
            if (remoteIp == null) return false;
            var bytes = remoteIp.GetAddressBytes();
            if (remoteIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (bytes.Length >= 1 && bytes[0] == 127) return true;                    // 127.0.0.0/8
                if (bytes.Length >= 1 && bytes[0] == 10) return true;                     // 10.0.0.0/8
                if (bytes.Length >= 2 && bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true; // 172.16.0.0/12
                if (bytes.Length >= 2 && bytes[0] == 192 && bytes[1] == 168) return true; // 192.168.0.0/16
                return false;
            }
            if (remoteIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (IPAddress.IPv6Loopback.Equals(remoteIp)) return true;
                if (bytes.Length >= 2 && bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true; // fe80::/10 link-local
                if (bytes.Length >= 1 && (bytes[0] & 0xfe) == 0xfc) return true;                      // fc00::/7 unique local
                return false;
            }
            return false;
        }

        private static bool DevKeyMatches(HttpContext httpContext)
        {
            var key = AppInit.conf.devkey;
            if (string.IsNullOrEmpty(key)) return true;
            if (httpContext.Request.Headers.TryGetValue("X-Dev-Key", out var h) && h == key) return true;
            var match = Regex.Match(httpContext.Request.QueryString.Value ?? "", "(\\?|&)devkey=([^&]+)");
            return match.Success && match.Groups[2].Value == key;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            bool fromLocalNetwork = IsLocalOrPrivate(httpContext.Connection.RemoteIpAddress);
            string path = httpContext.Request.Path.Value ?? "";

            if (!fromLocalNetwork)
            {
                // Внешние запросы: /cron/, /jsondb, /dev/ — только из локальной сети (запрет для lampa.mx и т.п.)
                if (IsLocalOnlyPath(path))
                {
                    httpContext.Response.StatusCode = httpContext.Request.Method == "OPTIONS" ? 204 : 403;
                    return;
                }

                // External: require API key when configured
                if (!string.IsNullOrEmpty(AppInit.conf.apikey))
                {
                    if (httpContext.Request.Path.Value == "/" || Regex.IsMatch(httpContext.Request.Path.Value, "^/(api/v1\\.0/conf|stats/|sync/)"))
                    {
                        await _next(httpContext);
                        return;
                    }

                    var match = Regex.Match(httpContext.Request.QueryString.Value ?? "", "(\\?|&)apikey=([^&]+)");
                    if (!match.Success || AppInit.conf.apikey != match.Groups[2].Value)
                    {
                        httpContext.Response.StatusCode = httpContext.Request.Method == "OPTIONS" ? 204 : 401;
                        return;
                    }
                }
            }
            else
            {
                // За туннелем / прокси все запросы выглядят локальными — ограничиваем /dev/, /cron/, /jsondb по devkey
                if (IsLocalOnlyPath(path) && !string.IsNullOrEmpty(AppInit.conf.devkey) && !DevKeyMatches(httpContext))
                {
                    httpContext.Response.StatusCode = httpContext.Request.Method == "OPTIONS" ? 204 : 401;
                    return;
                }
            }

            // Access-Control-Allow-Private-Network — не ставим для ответов 403/401 по локальным путям
            if (fromLocalNetwork || !IsLocalOnlyPath(path))
                httpContext.Response.Headers["Access-Control-Allow-Private-Network"] = "true";

            bool isCron = path.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase);
            var cronStopwatch = isCron ? Stopwatch.StartNew() : null;

            await _next(httpContext);

            if (isCron && cronStopwatch != null)
            {
                cronStopwatch.Stop();
                var label = path.Length > 6 && path.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase) ? path.Substring(6) : path.TrimStart('/');
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
