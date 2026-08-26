using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Leproduction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/leproduction/[action]")]
    public class LeproductionController : BaseController
    {
        readonly LeproductionSyncService _syncService;

        public LeproductionController(IMemoryCache memoryCache, LeproductionSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Parses torrent releases from le-production.online category pages.
        /// </summary>
        /// <param name="limit_page">Max pages per category. If 0 or less, detects last page from pagination.</param>
        /// <returns>
        /// "work" if already running, "canceled", "config missing", or "ok".
        /// </returns>
        [HttpGet]
        async public Task<string> Parse(int limit_page = 0)
        {
            return await _syncService.ParseAsync(limit_page);
        }
    }
}
