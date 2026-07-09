using Microsoft.AspNetCore.Mvc;
using JacRed.Application.Search;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.Tasks;

namespace JacRed.Controllers
{
    public class TorrentsController : BaseController
    {
        readonly ITorrentQueryService _torrentQueryService;

        public TorrentsController(IMemoryCache memoryCache, ITorrentQueryService torrentQueryService) : base(memoryCache)
        {
            _torrentQueryService = torrentQueryService;
        }

        #region Torrents
        [Route("/api/v1.0/torrents")]
        async public Task<JsonResult> Torrents(string search, string altname, bool exact, string type, string sort, string tracker, string voice, string videotype, long relased, long quality, long season)
        {
            var result = await _torrentQueryService.QueryTorrentsAsync(search, altname, exact, type, sort, tracker, voice, videotype, relased, quality, season, memoryCache);
            return Json(result);
        }

        [Route("/api/v1.0/qualitys")]
        public JsonResult Qualitys(string name, string originalname, string type, int page = 1, int take = 1000)
        {
            return Json(_torrentQueryService.QueryQualitys(name, originalname, type, page, take));
        }
        #endregion
    }
}
