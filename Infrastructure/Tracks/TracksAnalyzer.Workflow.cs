using JacRed.Infrastructure.Persistence;
using JacRed.Models.Tracks;
using MonoTorrent;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Tracks
{
    internal static partial class TracksAnalyzer
    {
        internal static async Task Add(string magnet, int currentAttempt, string[] types = null, string torrentKey = null, int typetask = 1, int sid = 0)
        {
            if (string.IsNullOrWhiteSpace(magnet))
            {
                TracksDB.Log("Ошибка: magnet-ссылка не может быть пустой", typetask);
                return;
            }

            if (types != null && theBad(types))
            {
                string msg = $"Пропуск добавления треков: недопустимый тип контента [{string.Join(", ", types)}]";
                TracksDB.Log(msg, typetask);
                return;
            }

            if (AppInit.conf?.tsuri == null || AppInit.conf.tsuri.Length == 0)
            {
                TracksDB.Log("Ошибка: не настроены tsuri серверы", typetask);
                return;
            }

            if (string.IsNullOrEmpty(AppInit.conf.trackscategory))
            {
                TracksDB.Log("Ошибка: не настроена trackscategory", typetask);
                return;
            }

            string infohash;
            try
            {
                infohash = TracksPathResolver.NormalizeInfohash(MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex());
                if (string.IsNullOrEmpty(infohash))
                {
                    TracksDB.Log("Ошибка: не удалось извлечь infohash из magnet-ссылки", typetask);
                    return;
                }
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка парсинга magnet-ссылки: {ex.Message}", typetask);
                return;
            }

            using var hashLock = await TryAcquireHashLockAsync(infohash, CancellationToken.None).ConfigureAwait(false);
            if (hashLock == null)
            {
                TracksDB.Log($"Торрент {infohash} уже анализируется — пропуск.", typetask);
                return;
            }

            RegisterInFlight(infohash);
            try
            {
                await AddCore(magnet, currentAttempt, types, torrentKey, typetask, sid, infohash)
                    .ConfigureAwait(false);
            }
            finally
            {
                UnregisterInFlight(infohash);
            }
        }

        static async Task AddCore(string magnet, int currentAttempt, string[] types, string torrentKey,
            int typetask, int sid, string infohash)
        {
            TracksDB.Log($"Начало анализа треков для {infohash}.", typetask);

            FfprobeModel res = null;
            string tsuri = null;
            string expectedCategory = AppInit.conf.trackscategory;
            bool analysisSuccessful = false;
            bool skipResultUpdate = false;
            bool ownedInCategory = false;
            string errorMessage = null;
            int apiStatusCode = 0;

            using (var pickCts = new CancellationTokenSource(TimeSpan.FromMinutes(1)))
            {
                tsuri = await SelectBestServer(pickCts.Token).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(tsuri))
            {
                TracksDB.Log("Все серверы недоступны.", typetask);
            }
            else
            {
                using (await AcquireAnalyzeSlotAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    try
                    {
                        var ffpTimeoutBase = GetFfpTimeout(sid);
                        var overallTimeout = GetAnalyzeOverallTimeout(sid);

                        using (var cancellationTokenSource = new CancellationTokenSource())
                        {
                            cancellationTokenSource.CancelAfter(overallTimeout);
                            var token = cancellationTokenSource.Token;

                            (bool torrentAdded, bool torrentExistsInCorrectCategory, bool serverError, bool addAttempted) =
                                await AddTorrentToServer(tsuri, magnet, infohash, expectedCategory, token, typetask)
                                    .ConfigureAwait(false);

                            if (serverError)
                            {
                                ownedInCategory = addAttempted || torrentExistsInCorrectCategory;
                                errorMessage = "TorrServer недоступен или таймаут add/list";
                                apiStatusCode = 503;
                                skipResultUpdate = true;
                                TracksDB.Log($"{errorMessage}.", typetask);
                            }
                            else
                            {
                                bool shouldAnalyze = torrentAdded || torrentExistsInCorrectCategory;
                                ownedInCategory = shouldAnalyze || addAttempted;

                                if (!shouldAnalyze)
                                {
                                    errorMessage = $"Торрент не в категории '{expectedCategory}'";
                                    TracksDB.Log($"{errorMessage}. Анализ отменен.", typetask);
                                    skipResultUpdate = true;
                                }
                                else
                                {
                                    if (torrentExistsInCorrectCategory)
                                        TracksDB.Log($"Торрент {infohash} уже существует на сервере в категории '{expectedCategory}'. Начинаем анализ...", typetask);
                                    else
                                        TracksDB.Log($"Торрент {infohash} успешно добавлен в категорию '{expectedCategory}'. Начинаем анализ...", typetask);

                                    await WaitTorrentReady(tsuri, infohash, token, typetask).ConfigureAwait(false);

                                    bool canProbe = await WaitDownloadProgress(tsuri, infohash, token, typetask)
                                        .ConfigureAwait(false);

                                    if (!canProbe)
                                    {
                                        errorMessage = "нет данных от сидов — /ffp пропущен";
                                        apiStatusCode = 408;
                                        TracksDB.Log($"{errorMessage} для {infohash}", typetask);
                                    }
                                    else
                                    {
                                        var peerInfo = await GetTorrentFromServer(tsuri, infohash, token, typetask)
                                            .ConfigureAwait(false);

                                        await WaitMediaBuffer(tsuri, infohash, token, typetask).ConfigureAwait(false);

                                        var ffpTimeout = GetFfpTimeout(sid, peerInfo);
                                        int maxFiles = 1 + GetFfpRetryExtra();
                                        var fileStats = peerInfo?.file_stats;
                                        var fileIds = TracksMediaFileSelector.SelectFileIds(fileStats, maxFiles);

                                        if (fileStats != null && fileIds.Count > 0)
                                        {
                                            var first = fileStats.FirstOrDefault(f => f.id == fileIds[0]);
                                            if (first != null)
                                            {
                                                TracksDB.Log(
                                                    $"Выбран file id={first.id} path={first.path} length={first.length}",
                                                    typetask);
                                            }
                                        }

                                        (res, apiStatusCode, errorMessage) = await ProbeFfpWithRetries(
                                            tsuri, infohash, fileIds, ffpTimeout, token, typetask)
                                            .ConfigureAwait(false);

                                        if (res?.streams != null && res.streams.Count > 0)
                                        {
                                            analysisSuccessful = true;
                                            TracksDB.Log($"API успешно вернул {res.streams.Count} треков", typetask);
                                        }
                                        else if (string.IsNullOrEmpty(errorMessage))
                                        {
                                            errorMessage = "Нет данных о треках";
                                            TracksDB.Log($"{errorMessage} для инфохаша {infohash} (код: {apiStatusCode})", typetask);
                                        }
                                        else if (errorMessage == "no probeable media file")
                                        {
                                            errorMessage = "нет подходящего media-файла для /ffp";
                                            TracksDB.Log($"{errorMessage} для {infohash}", typetask);
                                        }
                                        else
                                        {
                                            TracksDB.Log($"{errorMessage} для инфохаша {infohash} (код: {apiStatusCode})", typetask);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        errorMessage = $"Анализ для инфохаша {infohash} отменен по таймауту (overall {GetAnalyzeOverallTimeout(sid).TotalSeconds:F0}s)";
                        TracksDB.Log(errorMessage, typetask);
                        apiStatusCode = 408;
                    }
                    catch (JsonException ex)
                    {
                        errorMessage = $"Ошибка обработки JSON ответа: {ex.Message}";
                        TracksDB.Log(errorMessage, typetask);
                    }
                    catch (Exception ex)
                    {
                        errorMessage = $"Критическая ошибка при анализе треков: {ex.Message}";
                        TracksDB.Log(errorMessage, typetask);
                        TracksDB.LogToFile($"StackTrace: {ex.StackTrace}", typetask);
                    }
                    finally
                    {
                        if (!string.IsNullOrEmpty(tsuri))
                            await CleanupTorrent(tsuri, infohash, expectedCategory, typetask, ownedInCategory)
                                .ConfigureAwait(false);
                    }
                }
            }

            if (string.IsNullOrEmpty(tsuri) || apiStatusCode == 503)
            {
                int backoffMs = Math.Max(AppInit.conf.tracksdelay, 10_000);
                TracksDB.Log($"Backoff {backoffMs}ms (TorrServer down/timeout).", typetask);
                await Task.Delay(backoffMs).ConfigureAwait(false);
                return;
            }

            if (skipResultUpdate)
                return;

            await UpdateAnalysisResults(magnet, torrentKey, infohash, currentAttempt, analysisSuccessful, res, typetask, apiStatusCode, errorMessage)
                .ConfigureAwait(false);

            if (!analysisSuccessful && (apiStatusCode == 400 || apiStatusCode == 408 || apiStatusCode == 504 || apiStatusCode >= 500))
            {
                int backoffMs = Math.Max(AppInit.conf.tracksdelay, 5_000);
                TracksDB.Log($"Backoff {backoffMs}ms after API {apiStatusCode} for {infohash}", typetask);
                await Task.Delay(backoffMs).ConfigureAwait(false);
            }
        }

        /// <summary>Next attempt counter after a failed probe. HTTP 400 is not terminal.</summary>
        internal static int NextFailureAttempt(int currentAttempt) => currentAttempt + 1;

        static async Task UpdateAnalysisResults(string magnet, string torrentKey, string infohash,
            int currentAttempt, bool analysisSuccessful, FfprobeModel ffprobeResult, int typetask, int apiStatusCode, string errorMessage = null)
        {
            try
            {
                if (analysisSuccessful)
                {
                    if (ffprobeResult?.streams != null && ffprobeResult.streams.Count > 0)
                        await SaveTrackResults(ffprobeResult, infohash, typetask);

                    TracksDB.Log($"Анализ треков для {infohash} успешно завершен!", typetask);

                    if (currentAttempt == 0)
                        return;

                    if (string.IsNullOrEmpty(torrentKey))
                        torrentKey = FindTorrentKeyByMagnet(magnet);

                    if (string.IsNullOrEmpty(torrentKey))
                    {
                        TracksDB.Log($"Не удалось найти torrentKey для {infohash}. Сброс ffprobe_tryingdata невозможен.", typetask);
                        return;
                    }

                    FileDB.UpdateTorrentFfprobeInfo(torrentKey, magnet, 0);
                    return;
                }

                if (string.IsNullOrEmpty(torrentKey))
                {
                    torrentKey = FindTorrentKeyByMagnet(magnet);
                    if (string.IsNullOrEmpty(torrentKey))
                    {
                        TracksDB.Log($"Не удалось найти torrentKey для {infohash}. Обновление ffprobe_tryingdata невозможно.", typetask);
                        return;
                    }
                }

                int NewAttepmt = NextFailureAttempt(currentAttempt);

                if (NewAttepmt != currentAttempt)
                    FileDB.UpdateTorrentFfprobeInfo(torrentKey, magnet, NewAttepmt);

                LogAnalysisFailure(typetask, infohash, apiStatusCode, Math.Max(0, AppInit.conf.tracksatempt - NewAttepmt), errorMessage);
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка при обновлении результатов анализа: {ex.Message}", typetask);
            }
        }

        static string FindTorrentKeyByMagnet(string magnet)
        {
            try
            {
                var infohash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
                if (string.IsNullOrEmpty(infohash))
                    return null;

                foreach (var key in FileDB.masterDb.Keys)
                {
                    try
                    {
                        var db = FileDB.OpenRead(key, cache: false);
                        var torrent = db.Values.FirstOrDefault(t =>
                            !string.IsNullOrEmpty(t.magnet) &&
                            MagnetLink.Parse(t.magnet).InfoHashes.V1OrV2.ToHex() == infohash);

                        if (torrent != null)
                            return key;
                    }
                    catch (Exception ex)
                    {
                        TracksDB.Log($"FindTorrentKeyByMagnet scan key={key}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                TracksDB.Log($"FindTorrentKeyByMagnet: {ex.Message}");
            }

            return null;
        }

        static async Task SaveTrackResults(FfprobeModel result, string infohash, int? typetask = null)
        {
            if (result?.streams == null || result.streams.Count == 0)
                return;

            int audioCount = result.streams.Count(s => s.codec_type == "audio");
            int videoCount = result.streams.Count(s => s.codec_type == "video");

            TracksDB.Log($"Сохранение данных треков для {infohash}. Аудио: {audioCount}, видео: {videoCount}", typetask);

            try
            {
                Database.AddOrUpdate(infohash, result, (k, v) => result);
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка при обновлении данных в памяти: {ex.Message}", typetask);
            }

            try
            {
                string path = TracksPathResolver.pathDb(infohash, createfolder: true);
                string json = JsonConvert.SerializeObject(result, Formatting.Indented);
                await File.WriteAllTextAsync(path, json, Encoding.UTF8);
                TracksIndexManager.RegisterTrackHash(infohash);

                string legacyPath = TracksPathResolver.ResolveLegacyTrackPath(infohash);
                if (legacyPath != null && !string.Equals(path, legacyPath, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(legacyPath); }
                    catch { }
                }

                string uppercaseJson = TracksPathResolver.UppercaseLayoutPath("Data/tracks", infohash, withExtension: true);
                if (File.Exists(uppercaseJson) && !string.Equals(uppercaseJson, path, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(uppercaseJson); }
                    catch { }
                }

                var audioLanguages = result.streams
                    .Where(s => s.codec_type == "audio" && s.tags?.language != null)
                    .Select(s => s.tags.language)
                    .Distinct()
                    .ToList();

                if (audioLanguages.Any())
                {
                    TracksDB.Log($"Обнаружены аудио дорожки на языках: {string.Join(", ", audioLanguages)}", typetask);
                }
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка при сохранении данных в файл: {ex.Message}", typetask);
                TracksDB.LogToFile($"StackTrace: {ex.StackTrace}", typetask);
            }
        }
    }
}
