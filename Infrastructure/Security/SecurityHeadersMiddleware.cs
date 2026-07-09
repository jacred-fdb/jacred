using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Security
{
    public sealed class SecurityHeadersMiddleware
    {
        readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            SecurityHeaders.Apply(httpContext);
            await _next(httpContext);
        }
    }
}
