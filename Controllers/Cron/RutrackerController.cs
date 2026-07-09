using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Infrastructure.Trackers.Rutracker;
using Microsoft.AspNetCore.Mvc;
using JacRed.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [JacRedAuthorize(JacRedAccessPolicy.DevAdmin)]
    [Route("/cron/rutracker/[action]")]
    public class RutrackerController : BaseController
    {
        readonly RutrackerSyncService _syncService;

        public RutrackerController(IMemoryCache memoryCache, RutrackerSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        async public Task<string> Parse(int page)
        {
            return await _syncService.ParseAsync(page);
        }

        async public Task<string> UpdateTasksParse()
        {
            return await _syncService.UpdateTasksParseAsync();
        }

        async public Task<string> ParseAllTask()
        {
            return await _syncService.ParseAllTaskAsync();
        }

        async public Task<string> ParseLatest(int pages = 5)
        {
            return await _syncService.ParseLatestAsync(pages);
        }
    }
}
