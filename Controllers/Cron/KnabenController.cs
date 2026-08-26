using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Knaben;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// Knaben API v1 — TV + Movies from TPB, 1337x, EZTV, Rutracker.
    /// Config: init.yaml Knaben (host, parseDelay, useproxy).
    /// Parse: /cron/knaben/parse?pages=1&amp;orderBy=date&amp;orderDirection=desc — query, hours, orderBy, orderDirection, from, size, categories.
    /// Backfill: /cron/knaben/backfill?pages=10 — leaf subcategories asc→desc, state in Data/temp/knaben_backfill.json.
    /// Name: Call the Midwife S15E08→Call the Midwife; War.Machine.2026→War Machine; [2026, ...]→relased.
    /// Title normalized for FileDB (2160p, .HDR→ HDR). Migrate: /dev/FixKnabenNames.
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
            string orderDirection = "desc",
            string categories = null)
        {
            return await _syncService.ParseAsync(from, size, pages, query, hours, orderBy, orderDirection, categories);
        }

        /// <summary>
        /// Archive backfill by leaf TV/Movies subcategories (asc then desc, Knaben window ≤10000).
        /// Progress: Data/temp/knaben_backfill.json. Reset with ?reset=true.
        /// </summary>
        async public Task<string> Backfill(int size = 300, int pages = 10, bool reset = false)
        {
            return await _syncService.BackfillAsync(size, pages, reset);
        }

        /// <summary>Read-only backfill checkpoint summary.</summary>
        public string BackfillStatus() => _syncService.GetBackfillStatus();
    }
}
