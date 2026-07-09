using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers
{
    public class BaseController : Controller
    {
        public IMemoryCache memoryCache { get; }

        protected BaseController(IMemoryCache memoryCache)
        {
            this.memoryCache = memoryCache;
        }

        public JsonResult OnError(string msg)
        {
            return new JsonResult(new { success = false, msg });
        }
    }
}
