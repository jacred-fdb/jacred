using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Bitru;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// Парсинг Bitru через официальный API (api.php?get=torrents).
    /// Лимит: макс. 5 запросов в сек на IP — между запросами задержка 250 ms.
    /// </summary>
    [Route("/cron/bitruapi/[action]")]
    public class BitruApiController : BaseController
    {
        readonly BitruApiSyncService _syncService;

        public BitruApiController(IMemoryCache memoryCache, BitruApiSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        async public Task<string> Parse(int limit = 100) =>
            await _syncService.ParseAsync(limit);

        async public Task<string> ParseFromDate(string lastnewtor, int limit = 100) =>
            await _syncService.ParseFromDateAsync(lastnewtor, limit);
    }
}
