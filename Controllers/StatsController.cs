using JacRed.Infrastructure.Stats;
using JacRed.Infrastructure.Http;
using JacRed.Infrastructure.Tracks;
using Microsoft.AspNetCore.Mvc;
using System;

namespace JacRed.Controllers
{
    /// <summary>
    /// Stats API: /stats/torrents &amp; /stats/trackers — сводка; /stats/trackers/new|updated &amp; /{name}/new|updated — списки за сегодня.
    /// </summary>
    [Route("/stats/[action]")]
    public class StatsController : Controller
    {
        [Route("/stats/trackers")]
        public IActionResult Trackers()
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            return Content(StatsSummary.ReadAllJson(), "application/json");
        }

        [Route("/stats/trackers/new")]
        public IActionResult TrackersNewToday(int limit = 200)
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            return TorrentList(StatsTorrentIndex.Query.ForDay(StatsTorrentIndex.TorrentDayFilter.CreatedToday, limit: limit));
        }

        [Route("/stats/trackers/updated")]
        public IActionResult TrackersUpdatedToday(int limit = 200)
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            return TorrentList(StatsTorrentIndex.Query.ForDay(StatsTorrentIndex.TorrentDayFilter.UpdatedToday, limit: limit));
        }

        [Route("/stats/trackers/{trackerName}")]
        public IActionResult Tracker(string trackerName)
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            if (string.IsNullOrWhiteSpace(trackerName))
                return Content("[]", "application/json");

            return TrackerSummary(trackerName);
        }

        /// <summary>Сводка: без trackerName — все трекеры; с trackerName — один трекер.</summary>
        public IActionResult Torrents(string trackerName)
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            if (string.IsNullOrWhiteSpace(trackerName))
                return Content(StatsSummary.ReadAllJson(), "application/json");

            return TrackerSummary(trackerName);
        }

        static IActionResult TrackerSummary(string trackerName)
        {
            var row = StatsSummary.TryFindTracker(trackerName);
            if (row == null)
                return new NotFoundObjectResult(new { ok = false, error = "unknown tracker", trackerName });

            return new ContentResult
            {
                Content = row.ToString(Newtonsoft.Json.Formatting.None),
                ContentType = "application/json; charset=utf-8"
            };
        }

        [Route("/stats/meta")]
        public JsonResult Meta()
        {
            if (!AppInit.conf.openstats)
                return Json(new { ok = false });

            DateTime? updatedAt = StatsCollector.LastCollectedAtUtc ?? StatsCollector.TryReadStatsMetaUpdatedAt();
            var indexMeta = StatsTorrentIndex.TryLoadMeta();

            return Json(new
            {
                ok = true,
                updatedAt,
                updatedAtLocal = updatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                tracksStatsUpdatedAt = TracksDB.GetExportStatsUpdatedAt(),
                torrentIndexUpdatedAt = indexMeta?.updatedAt,
                torrentIndexEntries = indexMeta?.entryCount ?? 0,
                torrentIndexShardDay = indexMeta?.shardDay,
                torrentIndexCreateToday = indexMeta?.createTodayCount ?? 0,
                torrentIndexUpdateToday = indexMeta?.updateTodayCount ?? 0
            });
        }

        [Route("/stats/tracks")]
        public JsonResult Tracks(bool includeTorrentDb = true)
        {
            if (!AppInit.conf.openstats)
                return Json(new { ok = false });

            var stats = TracksDB.GetExportStats(includeTorrentDb, refresh: false);
            return Json(new
            {
                ok = true,
                updatedAt = TracksDB.GetExportStatsUpdatedAt(),
                fromCache = TracksDB.LastExportStatsFromCache,
                stats
            });
        }

        [Route("/stats/trackers/{trackerName}/new")]
        public IActionResult NewTorrents(string trackerName, int limit = 200)
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            if (string.IsNullOrWhiteSpace(trackerName))
                return Content("[]", "application/json");

            return TorrentList(StatsTorrentIndex.Query.ForDay(
                StatsTorrentIndex.TorrentDayFilter.CreatedToday, trackerName, limit));
        }

        [Route("/stats/trackers/{trackerName}/updated")]
        public IActionResult UpdatedTorrents(string trackerName, int limit = 200)
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            if (string.IsNullOrWhiteSpace(trackerName))
                return Content("[]", "application/json");

            return TorrentList(StatsTorrentIndex.Query.ForDay(
                StatsTorrentIndex.TorrentDayFilter.UpdatedToday, trackerName, limit));
        }

        static IActionResult TorrentList(StatsTorrentIndex.Query query) =>
            new StreamingStatsTorrentsResult(query);

    }
}
