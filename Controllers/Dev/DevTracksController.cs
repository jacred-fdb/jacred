using JacRed.Application.Dev;
using JacRed.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers.Dev
{
    [Route("/dev/[action]")]
    [JacRedAuthorize(JacRedAccessPolicy.DevAdmin)]
    public class DevTracksController : Controller
    {
        readonly ITracksAdminService _tracksAdminService;

        public DevTracksController(ITracksAdminService tracksAdminService)
        {
            _tracksAdminService = tracksAdminService;
        }

        public JsonResult TracksStats(bool includeTorrentDb = true, bool refresh = false) =>
            Json(_tracksAdminService.TracksStats(includeTorrentDb, refresh));

        public JsonResult ExportTracks(string dir = "Data/tracks-export", bool dryRun = false, bool includeTorrentDb = true, bool background = true) =>
            Json(_tracksAdminService.ExportTracks(dir, dryRun, includeTorrentDb, background));

        public JsonResult ExportTracksStatus() => Json(_tracksAdminService.ExportTracksStatus());

        public JsonResult BackfillTracks(bool dryRun = false, bool migrateLegacy = true, bool includeTorrentDb = true) =>
            Json(_tracksAdminService.BackfillTracks(dryRun, migrateLegacy, includeTorrentDb));
    }
}
