using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Anistar;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/anistar/[action]")]
    public class AnistarController : BaseController
    {
        readonly AnistarSyncService _syncService;

        public AnistarController(IMemoryCache memoryCache, AnistarSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Parses torrent releases from Anistar category pages (anime / hentai / dorams).
        /// </summary>
        /// <param name="limit_page">Max pages per category. If 0 or less, detects last page from listing HTML.</param>
        /// <returns>
        /// "work" if parsing is already in progress,
        /// "canceled" if the operation was canceled,
        /// "config missing" if host is empty,
        /// "empty" if every list page returned no posts,
        /// "ok" if parsing completed successfully.
        /// </returns>
        [HttpGet]
        async public Task<string> Parse(int limit_page = 0)
        {
            return await _syncService.ParseAsync(limit_page);
        }
    }
}
