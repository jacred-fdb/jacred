using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Rudub;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/rudub/[action]")]
    public class RudubController : BaseController
    {
        readonly RudubSyncService _syncService;

        public RudubController(IMemoryCache memoryCache, RudubSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Parses RuDub browse pages for HD 1080 / HD 2160 only (videoformat 4 and 5).
        /// </summary>
        /// <param name="parseFrom">Starting page (0-based site page).</param>
        /// <param name="parseTo">Ending page (inclusive). Used when set with <paramref name="parseFrom"/>.</param>
        /// <param name="limit_page">When &gt; 0 and parseFrom/parseTo are both 0: parse the first N pages (0..N-1), capped at 100.
        /// Hourly cron uses a small N; initial FDB fill: <c>?limit_page=50</c>.</param>
        [HttpGet]
        public async Task<string> Parse(int parseFrom = 0, int parseTo = 0, int limit_page = 0)
        {
            return await _syncService.ParseAsync(parseFrom, parseTo, limit_page);
        }
    }
}
