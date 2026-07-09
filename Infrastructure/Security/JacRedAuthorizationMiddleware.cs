using JacRed.Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Security
{
    public sealed class JacRedAuthorizationMiddleware
    {
        readonly RequestDelegate _next;
        readonly IJacRedAccessEvaluator _accessEvaluator;

        public JacRedAuthorizationMiddleware(RequestDelegate next, IJacRedAccessEvaluator accessEvaluator)
        {
            _next = next;
            _accessEvaluator = accessEvaluator;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            var path = httpContext.Request.Path.Value ?? "";
            var network = ClientNetworkContext.From(httpContext);
            var result = _accessEvaluator.EvaluatePath(path, httpContext);

            if (!result.IsAllowed)
            {
                if (result.SetPrivateNetworkHeaderOnDeny)
                    SetPrivateNetworkHeader(httpContext);
                httpContext.Response.StatusCode = result.DenyStatusCode;
                return;
            }

            if (_accessEvaluator.ShouldSetPrivateNetworkHeader(network, path))
                SetPrivateNetworkHeader(httpContext);

            var isCron = path.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase);
            var cronStopwatch = isCron ? Stopwatch.StartNew() : null;

            await _next(httpContext);

            if (isCron && cronStopwatch != null)
                LogCronRequest(path, cronStopwatch, httpContext.Response.StatusCode);
        }

        static void SetPrivateNetworkHeader(HttpContext ctx)
        {
            ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }

        static void LogCronRequest(string path, Stopwatch stopwatch, int status)
        {
            stopwatch.Stop();
            var label = path.Substring(6);
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            var elapsed = elapsedMs >= 1000
                ? $"{elapsedMs / 1000.0:F1}s"
                : $"{elapsedMs}ms";
            var ts = DateTime.Now.ToString("HH:mm:ss");
            var fail = status >= 400 ? " FAIL" : "";
            var level = status == 200 && JacRedLogSettings.CronSkipFastMs > 0 && elapsedMs < JacRedLogSettings.CronSkipFastMs
                ? LogLevel.Debug
                : LogLevel.Information;
            JacRedLog.Write(JacRedLogCategories.CronHttp, level, $"[{ts}] {label} {elapsed} {status}{fail}");
        }
    }
}
