using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Infrastructure.Trackers.Aniliberty;
using Microsoft.AspNetCore.Mvc;
using JacRed.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// Aniliberty API — anime torrents from aniliberty.top.
    /// Config: init.yaml Aniliberty (host, parseDelay, useproxy).
    /// Parse: /cron/aniliberty/parse?parseFrom=1&amp;parseTo=5
    /// </summary>
    [JacRedAuthorize(JacRedAccessPolicy.DevAdmin)]
    [Route("/cron/aniliberty/[action]")]
    public class AnilibertyController : BaseController
    {
        readonly AnilibertySyncService _syncService;

        public AnilibertyController(IMemoryCache memoryCache, AnilibertySyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        [HttpGet]
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0) =>
            await _syncService.ParseAsync(parseFrom, parseTo);
    }
}
