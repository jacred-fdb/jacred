using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Infrastructure.Trackers.Selezen;
using Microsoft.AspNetCore.Mvc;
using JacRed.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [JacRedAuthorize(JacRedAccessPolicy.DevAdmin)]
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
