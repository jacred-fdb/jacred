using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Korsars;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/korsars/[action]")]
    public class KorsarsController : BaseController
    {
        readonly KorsarsSyncService _syncService;

        public KorsarsController(IMemoryCache memoryCache, KorsarsSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Parse one zero-based listing page of every movie/serial/cartoon forum.
        /// Login required — listings expose inline magnets.
        /// </summary>
        async public Task<string> Parse(int page = 0) =>
            await _syncService.ParseAsync(page, HttpContext.RequestAborted);

        async public Task<string> UpdateTasksParse() =>
            await _syncService.UpdateTasksParseAsync();

        async public Task<string> ParseAllTask() =>
            await _syncService.ParseAllTaskAsync();

        /// <summary>Cheap daily pass: first N pages of every category (from task list).</summary>
        async public Task<string> ParseLatest(int pages = 5) =>
            await _syncService.ParseLatestAsync(pages, HttpContext.RequestAborted);
    }
}
