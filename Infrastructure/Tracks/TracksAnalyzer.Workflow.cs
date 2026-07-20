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
        internal static async Task Add(string magnet, int currentAttempt, string[] types = null, string torrentKey = null, int typetask = 1)
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

            TracksDB.Log($"Начало анализа треков для {infohash}.", typetask);

            FfprobeModel res = null;
            string tsuri = null;
            string expectedCategory = AppInit.conf.trackscategory;
            bool analysisSuccessful = false;
            bool skipResultUpdate = false;
            bool ownedInCategory = false;
            string errorMessage = null;
            int apiStatusCode = 0;

            using (await AcquireAnalyzeSlotAsync(CancellationToken.None).ConfigureAwait(false))
            {
                using (var cancellationTokenSource = new CancellationTokenSource())
                {
                    cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(1));
                    tsuri = await SelectBestServer(cancellationTokenSource.Token).ConfigureAwait(false);
                }

                if (string.IsNullOrEmpty(tsuri))
                {
                    TracksDB.Log("Все серверы недоступны. Пауза 1 минута...");
                    // Release slot before long backoff (Dispose of AcquireAnalyzeSlot on leaving using).
                }
                else
                {
                    try
                    {
                        using (var cancellationTokenSource = new CancellationTokenSource())
                        {
                            cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(3));
                            var token = cancellationTokenSource.Token;

                            (bool torrentAdded, bool torrentExistsInCorrectCategory, bool serverError) =
                                await AddTorrentToServer(tsuri, magnet, infohash, expectedCategory, token, typetask)
                                    .ConfigureAwait(false);

                            if (serverError)
                            {
                                errorMessage = "Сервер вернул ошибку при получении списка торрентов";
                                TracksDB.Log($"{errorMessage}. Пауза 1 минута...", typetask);
                                apiStatusCode = 503;
                                skipResultUpdate = true;
                            }
                            else
                            {
                                bool shouldAnalyze = torrentAdded || torrentExistsInCorrectCategory;
                                ownedInCategory = shouldAnalyze;

                                if (!shouldAnalyze)
                                {
                                    errorMessage = $"Торрент не в категории '{expectedCategory}'";
                                    TracksDB.Log($"{errorMessage}. Анализ отменен.", typetask);
                                    // Do not burn attempts — torrent owned by another category/instance.
                                    skipResultUpdate = true;
                                }
                                else
                                {
                                    if (torrentExistsInCorrectCategory)
                                        TracksDB.Log($"Торрент {infohash} уже существует на сервере в категории '{expectedCategory}'. Начинаем анализ...", typetask);
                                    else
                                        TracksDB.Log($"Торрент {infohash} успешно добавлен в категорию '{expectedCategory}'. Начинаем анализ...", typetask);

                                    if (torrentAdded)
                                        await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);

                                    (res, apiStatusCode) = await AnalyzeWithExternalApi(tsuri, infohash, token, typetask)
                                        .ConfigureAwait(false);

                                    if (res?.streams != null && res.streams.Count > 0)
                                    {
                                        analysisSuccessful = true;
                                        TracksDB.Log($"API успешно вернул {res.streams.Count} треков", typetask);
                                    }
                                    else
                                    {
                                        errorMessage = "Нет данных о треках";
                                        TracksDB.Log($"{errorMessage} для инфохаша {infohash} (код: {apiStatusCode})", typetask);
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        errorMessage = $"Анализ для инфохаша {infohash} отменен по таймауту (3 минуты)";
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

            if (string.IsNullOrEmpty(tsuri))
            {
                await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                TracksDB.Log("Пауза завершена. Выход.");
                return;
            }

            if (apiStatusCode == 503)
            {
                await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                TracksDB.Log("Пауза завершена. Выход.", typetask);
                return;
            }

            if (skipResultUpdate)
                return;

            await UpdateAnalysisResults(magnet, torrentKey, infohash, currentAttempt, analysisSuccessful, res, typetask, apiStatusCode, errorMessage)
                .ConfigureAwait(false);

            // Soften transient TS failures: brief pause before the next cron item.
            if (!analysisSuccessful && (apiStatusCode == 408 || apiStatusCode >= 500))
            {
                int backoffMs = Math.Max(AppInit.conf.tracksdelay, 5_000);
                TracksDB.Log($"Backoff {backoffMs}ms after API {apiStatusCode} for {infohash}", typetask);
                await Task.Delay(backoffMs).ConfigureAwait(false);
            }
        }

        static async Task UpdateAnalysisResults(string magnet, string torrentKey, string infohash,
            int currentAttempt, bool analysisSuccessful, FfprobeModel ffprobeResult, int typetask, int apiStatusCode, string errorMessage = null)
        {
            try
            {
                if (analysisSuccessful)
                {
                    // Canonical store is Data/tracks (+ index/RAM); FileDB.ffprobe is not written.
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

                int NewAttepmt = currentAttempt + 1;

                if (apiStatusCode == 400)
                    NewAttepmt = AppInit.conf.tracksatempt;

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
