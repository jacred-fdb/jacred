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

        public async Task Invoke(HttpContext httpContext)
        {
            httpContext.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            httpContext.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
            httpContext.Response.Headers["Access-Control-Allow-Headers"] = "Accept, Origin, Content-Type";
            httpContext.Response.Headers["Access-Control-Allow-Methods"] = "POST, GET, OPTIONS";

            if (httpContext.Request.Headers.TryGetValue("origin", out var origin))
                httpContext.Response.Headers["Access-Control-Allow-Origin"] = origin.ToString();
            else if (httpContext.Request.Headers.TryGetValue("referer", out var referer))
                httpContext.Response.Headers["Access-Control-Allow-Origin"] = referer.ToString();
            else
                httpContext.Response.Headers["Access-Control-Allow-Origin"] = "*";

            bool fromLocalNetwork = IsLocalOrPrivate(httpContext.Connection.RemoteIpAddress);

            if (!fromLocalNetwork)
            {
                // External: restrict /cron/, /jsondb, /dev/ to local only
                if (httpContext.Request.Path.Value.StartsWith("/cron/") || httpContext.Request.Path.Value.StartsWith("/jsondb") || httpContext.Request.Path.Value.StartsWith("/dev/"))
                    return;

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
                        return;
                }
            }

            bool isCron = httpContext.Request.Path.Value?.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase) == true;
            var cronStopwatch = isCron ? Stopwatch.StartNew() : null;

            await _next(httpContext);

            if (isCron && cronStopwatch != null)
            {
                cronStopwatch.Stop();
                var path = httpContext.Request.Path.Value ?? "";
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
