using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Viruseproject;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/viruseproject/[action]")]
    public class ViruseprojectController : BaseController
    {
        readonly ViruseprojectSyncService _syncService;

        public ViruseprojectController(IMemoryCache memoryCache, ViruseprojectSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Parses torrent releases from viruseproject.tv category pages.
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
