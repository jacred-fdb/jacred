using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.AnimeLayer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/animelayer/[action]")]
    public class AnimeLayerController : BaseController
    {
        readonly AnimeLayerSyncService _syncService;

        public AnimeLayerController(IMemoryCache memoryCache, AnimeLayerSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        [HttpGet]
        async public Task<bool> TakeLogin() => await _syncService.TakeLoginAsync();

        [HttpGet]
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0) => await _syncService.ParseAsync(parseFrom, parseTo);
    }
}
