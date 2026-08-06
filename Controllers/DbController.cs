using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using JacRed.Infrastructure.Persistence;
using System.Threading;

namespace JacRed.Controllers
{
    [Route("/jsondb/[action]")]
    public class DbController : BaseController
    {
        public DbController(IMemoryCache memoryCache) : base(memoryCache) { }

        static int _saveDbWork;

        public string Save()
        {
            if (Interlocked.CompareExchange(ref _saveDbWork, 1, 0) != 0)
                return "work";

            try
            {
                if (!string.IsNullOrWhiteSpace(AppInit.conf.syncapi))
                    return "syncapi";

                FileDB.SaveChangesToFile();
                return "ok";
            }
            finally
            {
                Interlocked.Exchange(ref _saveDbWork, 0);
            }
        }
    }
}
