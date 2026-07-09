using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using JacRed.Engine;

namespace JacRed.Controllers
{
    [Route("/jsondb/[action]")]
    public class DbController : BaseController
    {
        public DbController(IMemoryCache memoryCache) : base(memoryCache) { }

        static bool _saveDbWork = false;

        public string Save()
        {
            if (_saveDbWork)
                return "work";

            if (!string.IsNullOrWhiteSpace(AppInit.conf.syncapi))
                return "syncapi";

            _saveDbWork = true;

            FileDB.SaveChangesToFile();

            _saveDbWork = false;
            return "ok";
        }
    }
}
