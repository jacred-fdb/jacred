using JacRed.Engine;
using Microsoft.AspNetCore.Mvc;
using JacRed.Controllers.Filters;

namespace JacRed.Controllers.Dev
{
    [Route("/dev/[action]")]
    [LocalhostOnly]
    public class DevTracksController : Controller
    {
        /// <summary>
        /// Статистика по ffprobe/tracks (файлы Data/tracks + поле ffprobe в FileDB).
        /// </summary>
        public JsonResult TracksStats(bool includeTorrentDb = true, bool refresh = false)
        {

            var stats = TracksDB.GetExportStats(includeTorrentDb, refresh);
            return Json(new
            {
                ok = true,
                updatedAt = TracksDB.GetExportStatsUpdatedAt(),
                fromCache = TracksDB.LastExportStatsFromCache,
                stats
            });
        }

        /// <summary>
        /// Экспорт всех ffprobe/tracks в JSON (layout для lampa-tracks: AA/B/HASH.json).
        /// dryRun=true — только статистика; иначе по умолчанию фоновый экспорт (background=true).
        /// </summary>
        public JsonResult ExportTracks(string dir = "Data/tracks-export", bool dryRun = false, bool includeTorrentDb = true, bool background = true)
        {

            if (dryRun)
            {
                var result = TracksDB.ExportAll(dir, dryRun: true, includeTorrentDb);
                return Json(new { ok = true, result });
            }

            if (!background)
            {
                var result = TracksDB.ExportAll(dir, dryRun: false, includeTorrentDb);
                return Json(new { ok = true, result });
            }

            if (!TracksDB.TryStartExport(dir, includeTorrentDb))
                return Json(new { ok = false, alreadyRunning = true, status = TracksDB.GetExportJobStatus() });

            return Json(new { ok = true, started = true, status = TracksDB.GetExportJobStatus() });
        }

        /// <summary>
        /// Статус фонового экспорта tracks (см. ExportTracks).
        /// </summary>
        public JsonResult ExportTracksStatus()
        {

            return Json(new { ok = true, status = TracksDB.GetExportJobStatus() });
        }

        /// <summary>
        /// Backfill в Data/tracks: .json для новых файлов, миграция legacy → canonical lowercase layout, данные из FileDB.
        /// </summary>
        public JsonResult BackfillTracks(bool dryRun = false, bool migrateLegacy = true, bool includeTorrentDb = true)
        {

            var result = TracksDB.BackfillTracks("Data/tracks", dryRun, includeTorrentDb, migrateLegacy);
            return Json(new { ok = true, result });
        }
    }
}
