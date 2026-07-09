using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Infrastructure.Trackers.Rutor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/rutor/[action]")]
    public class RutorController : BaseController
    {
        readonly RutorSyncService _syncService;

        public RutorController(IMemoryCache memoryCache, RutorSyncService syncService) : base(memoryCache)
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
