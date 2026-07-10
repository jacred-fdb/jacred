using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using MonoTorrent;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace JacRed.Infrastructure.Tracks
{
    internal static class TracksExportService
    {
        static volatile bool _exportRunning;
        static readonly object _exportLock = new object();
        static TracksExportJobStatus _exportJob = new TracksExportJobStatus();

        /// <summary>
        /// Resolves and validates export output directory — must stay under <c>Data/</c>.
        /// </summary>
        static string ResolveExportOutputDir(string outputDir)
        {
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new ArgumentException("Export output directory is required.", nameof(outputDir));

            if (outputDir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new ArgumentException("Export output directory contains invalid path characters.", nameof(outputDir));

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.IsPathRooted(outputDir)
                    ? outputDir
                    : Path.Combine(Directory.GetCurrentDirectory(), outputDir));
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Export output directory is not a valid path.", nameof(outputDir), ex);
            }

            if (!TracksPathResolver.IsPathWithinDirectory("Data", fullPath))
                throw new ArgumentException("Export output directory must be inside Data.", nameof(outputDir));

            return fullPath;
        }

        /// <summary>
        /// R2 / lampa-tracks layout: {hash[0:2]}/{hash[2]}/{hash[3:40]}.json — lowercase hex.
        /// </summary>
        static string ExportFilePath(string outputDir, string infohash)
        {
            string path = TracksPathResolver.TrackLayoutPath(outputDir, infohash, withExtension: true);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }

        internal static Dictionary<string, FfprobeModel> CollectAll(bool includeTorrentDb = true)
        {
            var result = new Dictionary<string, FfprobeModel>(StringComparer.OrdinalIgnoreCase);
            CollectAllInto(result, null, includeTorrentDb);
            return result;
        }

        internal static void CollectAllInto(Dictionary<string, FfprobeModel> result, TracksExportStats stats, bool includeTorrentDb)
        {
            CollectFromTracksDir("Data/tracks", result, stats);

            foreach (var item in TracksAnalyzer.Database)
            {
                if (item.Value?.streams == null || item.Value.streams.Count == 0)
                    continue;

                if (!result.ContainsKey(item.Key))
                {
                    result[item.Key] = item.Value;
                    if (stats != null)
                        stats.fromMemory++;
                }
            }

            if (includeTorrentDb)
                CollectFromTorrentDb(result, stats);
        }

        static void CollectFromTracksDir(string tracksDir, Dictionary<string, FfprobeModel> result, TracksExportStats stats)
        {
            if (!Directory.Exists(tracksDir))
                return;

            foreach (var folder1 in Directory.GetDirectories(tracksDir))
            {
                foreach (var folder2 in Directory.GetDirectories(folder1))
                {
                    foreach (var file in Directory.GetFiles(folder2))
                    {
                        if (stats != null)
                            stats.filesScanned++;

                        string filename = Path.GetFileName(file);
                        if (TracksPathResolver.ShouldSkipLegacyTrackFile(folder2, filename))
                            continue;

                        string infohash = TracksPathResolver.InfohashFromTrackRelPath(
                            Path.GetFileName(folder1),
                            Path.GetFileName(folder2),
                            filename);

                        if (!TracksPathResolver.IsValidInfohash(infohash))
                        {
                            if (stats != null)
                                stats.invalidPath++;
                            continue;
                        }

                        try
                        {
                            var model = JsonConvert.DeserializeObject<FfprobeModel>(File.ReadAllText(file));
                            if (model?.streams == null || model.streams.Count == 0)
                            {
                                if (stats != null)
                                    stats.emptyStreams++;
                                continue;
                            }

                            result[infohash] = model;
                            if (stats != null)
                                stats.fromTracksFiles++;
                        }
                        catch
                        {
                            if (stats != null)
                                stats.readErrors++;
                        }
                    }
                }
            }
        }

        static void CollectFromTorrentDb(Dictionary<string, FfprobeModel> result, TracksExportStats stats)
        {
            foreach (var item in FileDB.masterDb.Keys)
            {
                IReadOnlyDictionary<string, TorrentDetails> db;
                try
                {
                    db = FileDB.OpenRead(item, cache: false);
                }
                catch
                {
                    if (stats != null)
                        stats.torrentDbErrors++;
                    continue;
                }

                if (db == null)
                    continue;

                foreach (var torrent in db.Values)
                {
                    if (stats != null)
                        stats.torrentsScanned++;

                    if (torrent?.ffprobe == null || torrent.ffprobe.Count == 0 || string.IsNullOrEmpty(torrent.magnet))
                        continue;

                    string infohash;
                    try
                    {
                        infohash = TracksPathResolver.NormalizeInfohash(MagnetLink.Parse(torrent.magnet).InfoHashes.V1OrV2.ToHex());
                    }
                    catch
                    {
                        if (stats != null)
                            stats.magnetErrors++;
                        continue;
                    }

                    if (!TracksPathResolver.IsValidInfohash(infohash))
                    {
                        if (stats != null)
                            stats.magnetErrors++;
                        continue;
                    }

                    if (result.ContainsKey(infohash))
                        continue;

                    result[infohash] = new FfprobeModel { streams = torrent.ffprobe };
                    if (stats != null)
                        stats.fromTorrentDb++;
                }
            }
        }

        static TracksExportStats CollectStatsOnly(bool includeTorrentDb) =>
            TracksStatsCache.BuildExportStats(includeTorrentDb, null);

        internal static TracksExportJobStatus GetExportJobStatus()
        {
            lock (_exportLock)
                return _exportJob.Clone();
        }

        /// <summary>
        /// Запускает ExportAll в фоне. Возвращает false, если экспорт уже идёт.
        /// </summary>
        internal static bool TryStartExport(string outputDir = "Data/tracks-export", bool includeTorrentDb = true)
        {
            outputDir = ResolveExportOutputDir(outputDir);

            lock (_exportLock)
            {
                if (_exportRunning)
                    return false;

                _exportRunning = true;
                _exportJob = new TracksExportJobStatus
                {
                    running = true,
                    phase = "collecting",
                    outputDir = outputDir,
                    includeTorrentDb = includeTorrentDb,
                    startedAt = DateTime.UtcNow
                };
            }

            var job = _exportJob;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var result = ExportAll(outputDir, dryRun: false, includeTorrentDb, job);
                    lock (_exportLock)
                    {
                        job.running = false;
                        job.phase = "done";
                        job.completedAt = DateTime.UtcNow;
                        job.result = result;
                        job.written = result.written;
                        job.writeErrors = result.writeErrors;
                        job.total = result.stats?.total ?? 0;
                    }
                }
                catch (Exception ex)
                {
                    lock (_exportLock)
                    {
                        job.running = false;
                        job.phase = "error";
                        job.completedAt = DateTime.UtcNow;
                        job.error = ex.Message;
                    }

                    JacRedLog.Error(JacRedLogCategories.TracksExport, ex.ToString());
                }
                finally
                {
                    _exportRunning = false;
                }
            });

            return true;
        }

        /// <summary>
        /// Экспорт всех ffprobe/tracks в JSON-файлы (layout JacRed → lampa-tracks).
        /// </summary>
        internal static TracksExportResult ExportAll(string outputDir = "Data/tracks-export", bool dryRun = false, bool includeTorrentDb = true, TracksExportJobStatus progress = null)
        {
            outputDir = ResolveExportOutputDir(outputDir);

            var result = new TracksExportResult
            {
                outputDir = outputDir,
                dryRun = dryRun,
                includeTorrentDb = includeTorrentDb
            };

            if (progress != null)
            {
                progress.phase = "collecting";
                progress.outputDir = outputDir;
                progress.includeTorrentDb = includeTorrentDb;
            }

            if (dryRun)
            {
                result.stats = CollectStatsOnly(includeTorrentDb);

                if (progress != null)
                {
                    progress.phase = "done";
                    progress.total = result.stats.total;
                    progress.stats = result.stats;
                }

                return result;
            }

            var data = new Dictionary<string, FfprobeModel>(StringComparer.OrdinalIgnoreCase);
            result.stats = new TracksExportStats();
            CollectAllInto(data, result.stats, includeTorrentDb);
            result.stats.total = data.Count;

            if (progress != null)
            {
                progress.phase = "writing";
                progress.total = data.Count;
                progress.stats = result.stats;
            }

            Directory.CreateDirectory(outputDir);

            foreach (var item in data)
            {
                if (!TracksPathResolver.IsValidInfohash(item.Key))
                {
                    result.writeErrors++;
                    continue;
                }

                try
                {
                    string path = ExportFilePath(outputDir, item.Key);
                    string json = JsonConvert.SerializeObject(item.Value, Formatting.Indented);
                    File.WriteAllText(path, json, Encoding.UTF8);
                    result.written++;

                    if (progress != null)
                        progress.written = result.written;
                }
                catch (Exception ex)
                {
                    result.writeErrors++;
                    if (result.errorSamples.Count < 10)
                        result.errorSamples.Add(new { hash = item.Key, error = ex.Message });

                    if (progress != null)
                        progress.writeErrors = result.writeErrors;
                }
            }

            try
            {
                string manifestPath = Path.Combine(outputDir, "export-manifest.json");
                File.WriteAllText(manifestPath, JsonConvert.SerializeObject(new
                {
                    exportedAt = DateTime.UtcNow,
                    outputDir,
                    includeTorrentDb,
                    result.stats,
                    result.written,
                    result.writeErrors
                }, Formatting.Indented), Encoding.UTF8);
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Backfill в Data/tracks: миграция legacy-файлов без расширения → .json и запись недостающих из FileDB.
        /// </summary>
        internal static TracksBackfillResult BackfillTracks(string tracksDir = "Data/tracks", bool dryRun = false, bool includeTorrentDb = true, bool migrateLegacy = true)
        {
            var result = new TracksBackfillResult
            {
                tracksDir = tracksDir,
                dryRun = dryRun,
                includeTorrentDb = includeTorrentDb,
                migrateLegacy = migrateLegacy
            };

            var data = new Dictionary<string, FfprobeModel>(StringComparer.OrdinalIgnoreCase);
            result.stats = new TracksExportStats();
            CollectAllInto(data, result.stats, includeTorrentDb);
            result.stats.total = data.Count;

            if (migrateLegacy)
                result.migratedLegacy = TracksPathResolver.MigrateTrackLayoutInPlace(tracksDir, dryRun);

            if (dryRun)
            {
                foreach (var item in data)
                {
                    if (TracksPathResolver.ResolveTrackJsonPath(item.Key, tracksDir) != null)
                        result.skippedExisting++;
                    else
                        result.written++;
                }

                return result;
            }

            Directory.CreateDirectory(tracksDir);

            foreach (var item in data)
            {
                try
                {
                    if (TracksPathResolver.ResolveTrackJsonPath(item.Key, tracksDir) != null)
                    {
                        result.skippedExisting++;
                        continue;
                    }

                    string jsonPath = TracksPathResolver.TrackLayoutPath(tracksDir, item.Key, withExtension: true);
                    Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));

                    string json = JsonConvert.SerializeObject(item.Value, Formatting.Indented);
                    File.WriteAllText(jsonPath, json, Encoding.UTF8);
                    TracksAnalyzer.Database.AddOrUpdate(item.Key, item.Value, (k, v) => item.Value);
                    TracksIndexManager.RegisterTrackHash(item.Key);
                    result.written++;
                }
                catch (Exception ex)
                {
                    result.writeErrors++;
                    if (result.errorSamples.Count < 10)
                        result.errorSamples.Add(new { hash = item.Key, error = ex.Message });
                }
            }

            try
            {
                string manifestPath = Path.Combine(tracksDir, "backfill-manifest.json");
                File.WriteAllText(manifestPath, JsonConvert.SerializeObject(new
                {
                    backfilledAt = DateTime.UtcNow,
                    tracksDir,
                    includeTorrentDb,
                    migrateLegacy,
                    result.stats,
                    result.written,
                    result.migratedLegacy,
                    result.skippedExisting,
                    result.writeErrors
                }, Formatting.Indented), Encoding.UTF8);
            }
            catch { }

            return result;
        }
    }
}
