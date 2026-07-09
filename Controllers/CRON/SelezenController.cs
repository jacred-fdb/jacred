using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Engine.Trackers.Selezen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/selezen/[action]")]
    public class SelezenController : BaseController
    {
        readonly SelezenSyncService _syncService;

        public SelezenController(IMemoryCache memoryCache, SelezenSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>Парсинг страниц. parseFrom/parseTo через query: /cron/selezen/parse?parseFrom=1&amp;parseTo=5. Если оба 0 — парсится одна страница 1.</summary>
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            return await _syncService.ParseAsync(parseFrom, parseTo);
        }
    }
}
