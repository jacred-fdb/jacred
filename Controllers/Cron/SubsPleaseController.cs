using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.SubsPlease;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// SubsPlease — public anime API (1080p magnets only).
    /// Parse: /cron/subsplease/parse?pages=2
    /// ParseShows: /cron/subsplease/ParseShows?limit=50 (Batches + backlog; checkpoint Data/temp/subsplease_shows.json)
    /// </summary>
    [Route("/cron/subsplease/[action]")]
    public class SubsPleaseController : BaseController
    {
        readonly SubsPleaseSyncService _syncService;

        public SubsPleaseController(IMemoryCache memoryCache, SubsPleaseSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <param name="pages">Latest API pages (first call has no p=; then p=1..). Default 2.</param>
        [HttpGet]
        public async Task<string> Parse(int pages = 2)
        {
            return await _syncService.ParseAsync(pages);
        }

        /// <param name="limit">Shows per run (default 50).</param>
        /// <param name="reset">Reset catalog checkpoint.</param>
        [HttpGet]
        public async Task<string> ParseShows(int limit = 50, bool reset = false)
        {
            return await _syncService.ParseShowsAsync(limit, reset);
        }

        [HttpGet]
        public string ParseShowStatus() => _syncService.GetParseShowStatus();
    }
}
