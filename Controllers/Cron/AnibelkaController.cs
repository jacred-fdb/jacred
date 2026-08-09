using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Anibelka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/anibelka/[action]")]
    public class AnibelkaController : BaseController
    {
        readonly AnibelkaSyncService _syncService;

        public AnibelkaController(IMemoryCache memoryCache, AnibelkaSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Parse one zero-based listing page of every anime forum section.
        /// Anonymous only — never logs in (passkey risk).
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
