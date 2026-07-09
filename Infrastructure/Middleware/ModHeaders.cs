using Microsoft.AspNetCore.Http;

namespace JacRed.Infrastructure.Middleware
{
    /// <summary>Backward-compatible facade for Startup (see Infrastructure/Security).</summary>
    public static class ModHeaders
    {
        public const string OriginalRemoteIpItemKey = Security.ClientNetworkContext.OriginalRemoteIpItemKey;

        public static void CaptureOriginalRemoteIp(HttpContext httpContext)
            => Security.ClientNetworkContext.CaptureOriginalRemoteIp(httpContext);

        public static void ApplySecurityHeaders(HttpContext httpContext)
            => Security.SecurityHeaders.Apply(httpContext);
    }
}
