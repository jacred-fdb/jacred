using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Infrastructure.Trackers.Knaben;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// Knaben API v1 — TV + Movies from TPB, 1337x, EZTV, Rutracker.
    /// Config: init.yaml Knaben (host, parseDelay, useproxy).
    /// Parse: /cron/knaben/parse?pages=1&amp;query=&amp;orderBy=date — query, hours, orderBy (date|seeders|peers).
    /// Name: Call the Midwife S15E08→Call the Midwife; War.Machine.2026→War Machine; [2026, ...]→relased.
    /// Title normalized for FileDB (2160p, .HDR→ HDR). Migrate: /dev/fixKnabenNames.
    /// </summary>
    [Route("/cron/knaben/[action]")]
    public class KnabenController : BaseController
    {
        readonly KnabenSyncService _syncService;

        public KnabenController(IMemoryCache memoryCache, KnabenSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        async public Task<string> Parse(
            int from = 0,
            int size = 300,
            int pages = 1,
            string query = null,
            int hours = 0,
            string orderBy = "date",
            string categories = null)
        {
            return await _syncService.ParseAsync(from, size, pages, query, hours, orderBy, categories);
        }
    }
}
