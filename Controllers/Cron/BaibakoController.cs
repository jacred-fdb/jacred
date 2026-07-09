using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Infrastructure.Trackers.Baibako;
using Microsoft.AspNetCore.Mvc;
using JacRed.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [JacRedAuthorize(JacRedAccessPolicy.DevAdmin)]
    [Route("/cron/baibako/[action]")]
    public class BaibakoController : BaseController
    {
        readonly BaibakoSyncService _syncService;

        public BaibakoController(IMemoryCache memoryCache, BaibakoSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Parses torrent releases from Baibako website pages.
        /// </summary>
        /// <param name="parseFrom">The starting page number to parse from. If 0 or less, defaults to page 0.</param>
        /// <param name="parseTo">The ending page number to parse to. If 0 or less, defaults to the same value as parseFrom.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a status string:
        /// - "work" if parsing is already in progress
        /// - "disabled" if host is not configured
        /// - "login error" if authorization failed
        /// - "ok" if parsing completed successfully
        /// </returns>
        [HttpGet]
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            return await _syncService.ParseAsync(parseFrom, parseTo);
        }
    }
}
