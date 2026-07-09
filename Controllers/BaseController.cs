using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace JacRed.Controllers
{
    public class BaseController : Controller, IDisposable
    {
        IServiceScope serviceScope;

        public IMemoryCache memoryCache { get; private set; }

        public BaseController()
        {
            serviceScope = Startup.ApplicationServices.CreateScope();
            var scopeServiceProvider = serviceScope.ServiceProvider;
            memoryCache = scopeServiceProvider.GetService<IMemoryCache>();
        }

        public JsonResult OnError(string msg)
        {
            return new JsonResult(new { success = false, msg });
        }

        public new void Dispose()
        {
            serviceScope?.Dispose();
            base.Dispose();
        }
    }
}
