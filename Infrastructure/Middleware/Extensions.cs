using JacRed.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;

namespace JacRed.Infrastructure.Middleware
{
    public static class Extensions
    {
        public static IApplicationBuilder UseModHeaders(this IApplicationBuilder builder)
        {
            return builder
                .UseMiddleware<SecurityHeadersMiddleware>()
                .UseMiddleware<JacRedAuthorizationMiddleware>();
        }
    }
}
