using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Toloka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/toloka/[action]")]
    public class TolokaController : BaseController
    {
        readonly TolokaSyncService _syncService;

        public TolokaController(IMemoryCache memoryCache, TolokaSyncService syncService) : base(memoryCache)
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
