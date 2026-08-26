using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Anifilm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/anifilm/[action]")]
    public class AnifilmController : BaseController
    {
        readonly AnifilmSyncService _syncService;

        public AnifilmController(IMemoryCache memoryCache, AnifilmSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Parses torrent releases from Anifilm category pages.
        /// </summary>
        /// <param name="fullparse">
        /// When true, uses larger per-category page limits (Go-compatible full crawl).
        /// When false (default), only the quick page window is scanned.
        /// </param>
        /// <returns>
        /// "work" if already running,
        /// "canceled" if canceled,
        /// "config missing" if host is empty,
        /// "ok" on success.
        /// </returns>
        [HttpGet]
        async public Task<string> Parse(bool fullparse = false)
        {
            return await _syncService.ParseAsync(fullparse);
        }
    }
}
