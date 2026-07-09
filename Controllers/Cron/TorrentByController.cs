using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Infrastructure.Trackers.TorrentBy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/torrentby/[action]")]
    public class TorrentByController : BaseController
    {
        readonly TorrentBySyncService _syncService;

        public TorrentByController(IMemoryCache memoryCache, TorrentBySyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        async public Task<string> Parse(int page) =>
            await _syncService.ParseAsync(page);

        async public Task<string> UpdateTasksParse() =>
            await _syncService.UpdateTasksParseAsync();

        async public Task<string> ParseAllTask() =>
            await _syncService.ParseAllTaskAsync();

        async public Task<string> ParseLatest(int pages = 5) =>
            await _syncService.ParseLatestAsync(pages);
    }
}
