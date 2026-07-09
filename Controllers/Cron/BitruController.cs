using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Infrastructure.Trackers.Bitru;
using Microsoft.AspNetCore.Mvc;
using JacRed.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [JacRedAuthorize(JacRedAccessPolicy.DevAdmin)]
    [Route("/cron/bitru/[action]")]
    public class BitruController : BaseController
    {
        readonly BitruSyncService _syncService;

        public BitruController(IMemoryCache memoryCache, BitruSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        async public Task<string> Parse(int page = 1) =>
            await _syncService.ParseAsync(page);

        async public Task<string> UpdateTasksParse() =>
            await _syncService.UpdateTasksParseAsync();

        async public Task<string> ParseAllTask() =>
            await _syncService.ParseAllTaskAsync();

        async public Task<string> ParseLatest(int pages = 5) =>
            await _syncService.ParseLatestAsync(pages);
    }
}
