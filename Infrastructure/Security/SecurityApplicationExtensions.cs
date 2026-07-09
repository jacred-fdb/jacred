using Microsoft.AspNetCore.Builder;

namespace JacRed.Infrastructure.Security
{
    public static class SecurityApplicationExtensions
    {
        public static IApplicationBuilder UseJacRedSecurity(this IApplicationBuilder builder)
        {
            return builder
                .UseMiddleware<SecurityHeadersMiddleware>()
                .UseMiddleware<JacRedAuthorizationMiddleware>();
        }
    }
}
