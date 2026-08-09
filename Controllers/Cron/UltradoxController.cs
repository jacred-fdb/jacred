using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Ultradox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/ultradox/[action]")]
    public class UltradoxController : BaseController
    {
        readonly UltradoxSyncService _syncService;

        public UltradoxController(IMemoryCache memoryCache, UltradoxSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Parse one listing page of every section (page≤0 = section root).
        /// Requests use a google Referer — own-origin Referer gets 503 from nginx.
        /// </summary>
        async public Task<string> Parse(int page = 0) =>
            await _syncService.ParseAsync(page, HttpContext.RequestAborted);

        async public Task<string> UpdateTasksParse() =>
            await _syncService.UpdateTasksParseAsync();

        async public Task<string> ParseAllTask() =>
            await _syncService.ParseAllTaskAsync();

        /// <summary>Cheap daily pass: first N pages of every section.</summary>
        async public Task<string> ParseLatest(int pages = 5) =>
            await _syncService.ParseLatestAsync(pages, HttpContext.RequestAborted);
    }
}
