using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Tracks;

namespace JacRed.Application.Dev
{
    public class TracksAdminService : ITracksAdminService
    {

        /// <summary>
        /// Статистика по ffprobe/tracks (файлы Data/tracks + поле ffprobe в FileDB).
        /// </summary>
        public object TracksStats(bool includeTorrentDb = true, bool refresh = false)
        {

            var stats = TracksDB.GetExportStats(includeTorrentDb, refresh);
            return new
            {
                ok = true,
                updatedAt = TracksDB.GetExportStatsUpdatedAt(),
                fromCache = TracksDB.LastExportStatsFromCache,
                stats
            };
        }

        /// <summary>
        /// Экспорт всех ffprobe/tracks в JSON (layout для lampa-tracks: AA/B/HASH.json).
        /// dryRun=true — только статистика; иначе по умолчанию фоновый экспорт (background=true).
        /// </summary>
        public object ExportTracks(string dir = "Data/tracks-export", bool dryRun = false, bool includeTorrentDb = true, bool background = true)
        {

            if (dryRun)
            {
                var result = TracksDB.ExportAll(dir, dryRun: true, includeTorrentDb);
                return new { ok = true, result };
            }

            if (!background)
            {
                var result = TracksDB.ExportAll(dir, dryRun: false, includeTorrentDb);
                return new { ok = true, result };
            }

            if (!TracksDB.TryStartExport(dir, includeTorrentDb))
                return new { ok = false, alreadyRunning = true, status = TracksDB.GetExportJobStatus() };

            return new { ok = true, started = true, status = TracksDB.GetExportJobStatus() };
        }

        /// <summary>
        /// Статус фонового экспорта tracks (см. ExportTracks).
        /// </summary>
        public object ExportTracksStatus()
        {

            return new { ok = true, status = TracksDB.GetExportJobStatus() };
        }

        /// <summary>
        /// Backfill в Data/tracks: .json для новых файлов, миграция legacy → canonical lowercase layout, данные из FileDB.
        /// </summary>
        public object BackfillTracks(bool dryRun = false, bool migrateLegacy = true, bool includeTorrentDb = true)
        {

            var result = TracksDB.BackfillTracks("Data/tracks", dryRun, includeTorrentDb, migrateLegacy);
            return new { ok = true, result };
        }

    }
}
