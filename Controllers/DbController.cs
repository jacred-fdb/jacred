using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Controllers
{
    [Route("/jsondb/[action]")]
    public class DbController : BaseController
    {
        public DbController(IMemoryCache memoryCache) : base(memoryCache) { }

        static int _saveDbWork;

        public string Save()
        {
            if (!string.IsNullOrWhiteSpace(AppInit.conf.syncapi))
                return "syncapi";

            if (Interlocked.CompareExchange(ref _saveDbWork, 1, 0) != 0)
                return "work";

            _ = Task.Run(() =>
            {
                try
                {
                    FileDB.SaveChangesToFile();
                    JacRedLog.Information(JacRedLogCategories.Fdb, "jsondb/save completed (background)");
                }
                catch (Exception ex)
                {
                    JacRedLog.Error(JacRedLogCategories.Fdb, $"jsondb/save error: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _saveDbWork, 0);
                }
            });

            return "ok";
        }
    }
}
