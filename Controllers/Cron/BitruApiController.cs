using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Bitru;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// Парсинг Bitru через официальный API (api.php?get=torrents).
    /// Лимит: макс. 5 запросов в сек на IP — между запросами задержка 250 ms.
    /// Live: request after_date = older-than (документация BitRu инвертирует имена).
    /// </summary>
    [Route("/cron/bitru/[action]")]
    public class BitruApiController : BaseController
    {
        readonly BitruApiSyncService _syncService;

        public BitruApiController(IMemoryCache memoryCache, BitruApiSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>Newest page only (regular cron).</summary>
        async public Task<string> Parse(int limit = 100) =>
            await _syncService.ParseAsync(limit, HttpContext.RequestAborted);

        /// <summary>
        /// Walk older archive with persisted cursor (Data/temp/bitru_backfill_cursor.txt).
        /// </summary>
        async public Task<string> Backfill(int pages = 20, int limit = 100) =>
            await _syncService.BackfillAsync(pages, limit, HttpContext.RequestAborted);

        /// <summary>
        /// Fetch torrents older than the given calendar day (dd.MM.yyyy).
        /// Live API after_date means older-than; continues via backfill cursor.
        /// </summary>
        async public Task<string> ParseFromDate(string lastnewtor, int limit = 100, int pages = 20) =>
            await _syncService.ParseFromDateAsync(lastnewtor, limit, pages, HttpContext.RequestAborted);
    }
}
