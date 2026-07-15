using JacRed.Infrastructure.Stats;
using JacRed.Infrastructure.Tracks;
using Microsoft.AspNetCore.Mvc;
using System;

namespace JacRed.Controllers
{
    /// <summary>
    /// Stats API for web UI: /stats/torrents (stats.json), /stats/meta (stats-meta.json timestamps).
    /// Tracks export remains on /dev/TracksStats and /sync/tracks/stats.
    /// </summary>
    [Route("/stats/[action]")]
    public class StatsController : Controller
    {
        /// <summary>Сводка по всем трекерам из Data/temp/stats.json (UI /stats).</summary>
        public IActionResult Torrents()
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            return Content(StatsSummary.ReadAllJson(), "application/json");
        }

        [Route("/stats/meta")]
        public JsonResult Meta()
        {
            if (!AppInit.conf.openstats)
                return Json(new { ok = false });

            DateTime? updatedAt = StatsCollector.LastCollectedAtUtc ?? StatsCollector.TryReadStatsMetaUpdatedAt();

            return Json(new
            {
                ok = true,
                updatedAt,
                updatedAtLocal = updatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                tracksStatsUpdatedAt = TracksDB.GetExportStatsUpdatedAt()
            });
        }
    }
}
