using System.Threading.Tasks;
using JacRed.Controllers;
using JacRed.Infrastructure.Trackers.Mazepa;
using Microsoft.AspNetCore.Mvc;
using JacRed.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [JacRedAuthorize(JacRedAccessPolicy.DevAdmin)]
    [Route("/cron/mazepa/[action]")]
    public class MazepaController : BaseController
    {
        readonly MazepaSyncService _syncService;

        public MazepaController(IMemoryCache memoryCache, MazepaSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        public async Task<string> Parse()
        {
            return await _syncService.ParseAsync();
        }
    }
}
