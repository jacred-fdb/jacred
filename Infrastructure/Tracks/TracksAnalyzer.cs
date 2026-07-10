using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using MonoTorrent;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Tracks
{
    internal static class TracksAnalyzer
    {
        internal static readonly ConcurrentDictionary<string, FfprobeModel> Database =
            new ConcurrentDictionary<string, FfprobeModel>();

        internal static bool theBad(string[] types)
        {
            if (types == null || types.Length == 0)
                return true;

            if (types.Contains("sport") || types.Contains("tvshow") || types.Contains("docuserial"))
                return true;

            return false;
        }

        /// <param name="magnet">Magnet URI of the torrent.</param>
        /// <param name="types">Content types; sport/tvshow/docuserial are skipped.</param>
        /// <param name="memoryOnly">If true, return streams only when already in the in-memory cache; skip disk lookup.</param>
        internal static List<ffStream> Get(string magnet, string[] types = null, bool memoryOnly = false)
        {
            if (types != null && theBad(types))
                return null;

            string infohash = TracksPathResolver.NormalizeInfohash(MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex());
            if (Database.TryGetValue(infohash, out FfprobeModel res))
                return res.streams;

            if (memoryOnly)
                return null;

            string path = TracksPathResolver.ResolveTrackPath(infohash);
            if (path == null)
                return null;

            try
            {
                res = JsonConvert.DeserializeObject<FfprobeModel>(File.ReadAllText(path));
                if (res?.streams == null || res.streams.Count == 0)
                    return null;
            }
            catch { return null; }

            Database.AddOrUpdate(infohash, res, (k, v) => res);
            TracksIndexManager.RegisterTrackHash(infohash);
            return res.streams;
        }

        /// <summary>
        /// Выбирает сервер с наименьшим количеством торрентов в указанной категории
        /// </summary>
        static async Task<string> SelectBestServer(CancellationToken token)
        {
            var servers = AppInit.conf.tsuri;
            if (servers == null || servers.Length == 0)
                return null;

            string expectedCategory = AppInit.conf.trackscategory;
            var serverTasks = new List<Task<(string server, int count, bool isValid)>>();

            foreach (var server in servers)
            {
                serverTasks.Add(GetServerTorrentCount(server, expectedCategory, token));
            }

            var results = await Task.WhenAll(serverTasks);

            var validServers = results.Where(r => r.isValid).ToList();

            if (validServers.Count == 0)
                return null;

            var bestServer = validServers.OrderBy(r => r.count).First();
            return bestServer.server;
        }

        /// <summary>
        /// Получает количество торрентов на сервере в указанной категории
        /// </summary>
        static async Task<(string server, int count, bool isValid)> GetServerTorrentCount(string server, string category, CancellationToken token)
        {
            try
            {
                (bool exists, string actualCategory, bool serverError) = await CheckTorrentExistsWithCategory(server, null, token);

                if (serverError)
                    return (server, 0, false);

                int count = await GetTorrentCountByCategory(server, category, token);

                return (server, count, true);
            }
            catch (Exception)
            {
                return (server, 0, false);
            }
        }

        /// <summary>
        /// Получает количество торрентов в указанной категории
        /// </summary>
        static async Task<int> GetTorrentCountByCategory(string tsuri, string category, CancellationToken token)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                AddBasicAuthHeader(client, tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "list"
                });

                var content = new System.Net.Http.StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await client.PostAsync($"{tsuri}/torrents", content, token);

                if (!response.IsSuccessStatusCode)
                    return 0;

                var jsonResponse = await response.Content.ReadAsStringAsync(token);
                var torrents = JsonConvert.DeserializeObject<List<TracksDB.TorrentInfo>>(jsonResponse);

                if (torrents == null || torrents.Count == 0)
                    return 0;

                return torrents.Count(t =>
                    !string.IsNullOrEmpty(t.category) &&
                    t.category.Equals(category, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Анализ медиа-треков торрента
        /// </summary>
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

            string tsuri;
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(1));
                var token = cancellationTokenSource.Token;

                tsuri = await SelectBestServer(token);
                if (string.IsNullOrEmpty(tsuri))
                {
                    TracksDB.Log("Все серверы недоступны. Пауза 1 минута...");
                    await Task.Delay(TimeSpan.FromMinutes(1), token);
                    TracksDB.Log("Пауза завершена. Выход.");
                    return;
                }
            }

            string expectedCategory = AppInit.conf.trackscategory;

            bool analysisSuccessful = false;
            string errorMessage = null;
            int apiStatusCode = 0;

            try
            {
                using (var cancellationTokenSource = new CancellationTokenSource())
                {
                    cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(3));
                    var token = cancellationTokenSource.Token;

                    (bool torrentAdded, bool torrentExistsInCorrectCategory, bool serverError) =
                        await AddTorrentToServer(tsuri, magnet, infohash, expectedCategory, token, typetask);

                    if (serverError)
                    {
                        errorMessage = "Сервер вернул ошибку при получении списка торрентов";
                        TracksDB.Log($"{errorMessage}. Пауза 1 минута...", typetask);

                        await Task.Delay(TimeSpan.FromMinutes(1), token);

                        TracksDB.Log("Пауза завершена. Выход.", typetask);
                        return;
                    }

                    bool shouldAnalyze = torrentAdded || torrentExistsInCorrectCategory;

                    if (!shouldAnalyze)
                    {
                        if (torrentExistsInCorrectCategory == false)
                        {
                            errorMessage = $"Торрент не в категории '{expectedCategory}'";
                            TracksDB.Log($"{errorMessage}. Анализ отменен.", typetask);
                        }
                        else
                        {
                            errorMessage = "Не удалось добавить торрент на сервер";
                            TracksDB.Log($"{errorMessage} и он не существует в категории '{expectedCategory}'. Завершение.", typetask);
                        }
                        return;
                    }

                    if (torrentExistsInCorrectCategory)
                    {
                        TracksDB.Log($"Торрент {infohash} уже существует на сервере в категории '{expectedCategory}'. Начинаем анализ...", typetask);
                    }
                    else if (torrentAdded)
                    {
                        TracksDB.Log($"Торрент {infohash} успешно добавлен в категорию '{expectedCategory}'. Начинаем анализ...", typetask);
                    }

                    if (torrentAdded)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), token);
                    }

                    (res, apiStatusCode) = await AnalyzeWithExternalApi(tsuri, infohash, token, typetask);

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
                await CleanupTorrent(tsuri, infohash, expectedCategory, typetask);
            }

            await UpdateAnalysisResults(magnet, torrentKey, infohash, currentAttempt, analysisSuccessful, res, typetask, apiStatusCode, errorMessage);
        }

        static async Task UpdateAnalysisResults(string magnet, string torrentKey, string infohash,
            int currentAttempt, bool analysisSuccessful, FfprobeModel ffprobeResult, int typetask, int apiStatusCode, string errorMessage = null)
        {
            try
            {
                if (string.IsNullOrEmpty(torrentKey))
                {
                    torrentKey = FindTorrentKeyByMagnet(magnet);
                    if (string.IsNullOrEmpty(torrentKey))
                    {
                        TracksDB.Log($"Не удалось найти torrentKey для {infohash}. Обновление ffprobe_tryingdata невозможно.", typetask);
                        return;
                    }
                }

                if (analysisSuccessful)
                {
                    if (ffprobeResult?.streams != null && ffprobeResult.streams.Count > 0)
                    {
                        await SaveTrackResults(ffprobeResult, infohash, typetask);
                    }

                    TracksDB.Log($"Анализ треков для {infohash} успешно завершен!", typetask);
                }
                else
                {
                    int NewAttepmt = currentAttempt;

                    if (typetask != 1)
                    {
                        NewAttepmt++;

                        if (apiStatusCode == 400)
                            NewAttepmt = AppInit.conf.tracksatempt;
                    }

                    if (NewAttepmt != currentAttempt)
                        FileDB.UpdateTorrentFfprobeInfo(torrentKey, magnet, NewAttepmt);

                    LogAnalysisFailure(typetask, infohash, apiStatusCode, AppInit.conf.tracksatempt - NewAttepmt, errorMessage);
                }
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
                    catch { }
                }
            }
            catch { }

            return null;
        }

        static void AddBasicAuthHeader(System.Net.Http.HttpClient client, string url)
        {
            try
            {
                var uri = new Uri(url);
                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    var credentials = uri.UserInfo.Split(':');
                    if (credentials.Length == 2)
                    {
                        string username = credentials[0];
                        string password = credentials[1];

                        var byteArray = Encoding.ASCII.GetBytes($"{username}:{password}");
                        var base64String = Convert.ToBase64String(byteArray);
                        client.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64String);

                        client.DefaultRequestHeaders.Accept.Clear();
                        client.DefaultRequestHeaders.Accept.Add(
                            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    }
                }
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка при добавлении Basic Auth: {ex.Message}");
            }
        }

        static async Task<(bool exists, string category, bool serverError)> CheckTorrentExistsWithCategory(
            string tsuri, string infohash, CancellationToken token, int? typetask = null)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                AddBasicAuthHeader(client, tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "list"
                });

                var content = new System.Net.Http.StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await client.PostAsync($"{tsuri}/torrents", content, token);

                if (!response.IsSuccessStatusCode)
                {
                    TracksDB.Log($"Сервер вернул ошибку при запросе списка торрентов: {(int)response.StatusCode}");
                    return (false, null, true);
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(token);

                var torrents = JsonConvert.DeserializeObject<List<TracksDB.TorrentInfo>>(jsonResponse);

                if (torrents == null)
                {
                    TracksDB.Log("Получен пустой список торрентов");
                    return (false, null, false);
                }

                if (string.IsNullOrEmpty(infohash))
                    return (false, null, false);

                var torrent = torrents.FirstOrDefault(t =>
                    (!string.IsNullOrEmpty(t.hash) &&
                     t.hash.Equals(infohash, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(t.name) &&
                     t.name.EndsWith(infohash, StringComparison.OrdinalIgnoreCase)));

                if (torrent == null)
                    return (false, null, false);

                string torrentCategory = torrent.category ?? string.Empty;

                return (true, torrentCategory, false);
            }
            catch (TaskCanceledException)
            {
                return (false, null, true);
            }
            catch (Exception)
            {
                return (false, null, true);
            }
        }

        static async Task<(bool added, bool existsInCorrectCategory, bool serverError)> AddTorrentToServer(
            string tsuri, string magnet, string infohash, string expectedCategory, CancellationToken token, int? typetask = null)
        {
            try
            {
                (bool exists, string actualCategory, bool serverError) = await CheckTorrentExistsWithCategory(tsuri, infohash, token, typetask);

                if (serverError)
                    return (false, false, true);

                if (exists)
                {
                    bool isCorrectCategory = actualCategory?.Equals(expectedCategory, StringComparison.OrdinalIgnoreCase) ?? false;

                    if (isCorrectCategory)
                        return (false, true, false);

                    return (false, false, false);
                }

                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                AddBasicAuthHeader(client, tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "add",
                    link = magnet,
                    save_to_db = false,
                    category = expectedCategory
                });

                var content = new System.Net.Http.StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await client.PostAsync($"{tsuri}/torrents", content, token);

                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();

                    byte[] buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                    }

                    return (true, false, false);
                }

                TracksDB.Log($"Ошибка при добавлении торрента ({(int)response.StatusCode})", typetask);
                return (false, false, false);
            }
            catch (TaskCanceledException)
            {
                TracksDB.Log("Таймаут при добавлении торрента на сервер", typetask);
                return (false, false, true);
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка при добавлении торрента на сервере: {ex.Message}", typetask);
                return (false, false, true);
            }
        }

        static async Task<(FfprobeModel result, int statusCode)> AnalyzeWithExternalApi(
            string tsuri, string infohash, CancellationToken token, int? typetask = null)
        {
            string apiUrl = $"{tsuri}/ffp/{infohash.ToUpper()}/1";

            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromMinutes(2);

            AddBasicAuthHeader(client, tsuri);

            using var response = await client.GetAsync(apiUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);

            int statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
                return (null, statusCode);

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var jsonBuilder = new StringBuilder();
            char[] buffer = new char[8192];
            int charsRead;

            while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                jsonBuilder.Append(buffer, 0, charsRead);
            }

            string jsonResponse = jsonBuilder.ToString();

            if (string.IsNullOrWhiteSpace(jsonResponse))
                return (null, statusCode);

            var result = JsonConvert.DeserializeObject<FfprobeModel>(jsonResponse);

            if (result == null)
                return (null, statusCode);

            return (result, statusCode);
        }

        static async Task CleanupTorrent(string tsuri, string infohash, string expectedCategory, int? typetask = null)
        {
            try
            {
                (bool exists, string actualCategory, bool serverError) = await CheckTorrentExistsWithCategory(tsuri, infohash, CancellationToken.None, typetask);

                if (serverError)
                {
                    TracksDB.Log($"Сервер вернул ошибку при запросе списка торрентов. Удаление отменено.", typetask);
                    return;
                }

                if (!exists)
                {
                    TracksDB.Log($"Торрент {infohash} не найден на сервере. Удаление не требуется.", typetask);
                    return;
                }

                bool isExpectedCategory = actualCategory?.Equals(expectedCategory, StringComparison.OrdinalIgnoreCase) ?? false;

                if (!isExpectedCategory)
                {
                    TracksDB.Log($"Торрент {infohash} не в категории '{expectedCategory}' (категория: '{actualCategory}'). Удаление отменено.", typetask);
                    return;
                }

                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                AddBasicAuthHeader(client, tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "rem",
                    hash = infohash
                });

                var content = new System.Net.Http.StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await client.PostAsync($"{tsuri}/torrents", content);

                if (response.IsSuccessStatusCode)
                {
                    TracksDB.Log($"Торрент {infohash} успешно удален с сервера", typetask);
                }
                else
                {
                    TracksDB.Log($"Ошибка при удалении торрента ({(int)response.StatusCode})", typetask);
                }
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка при очистке торрента {infohash} на сервере: {ex.Message}", typetask);
            }
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

        internal static void LogAnalysisFailure(int typetask, string infohash, int apiStatusCode, int remaining, string errorMessage)
        {
            var detail = $"Анализ треков для {infohash} без результата. Код ответа API: {apiStatusCode}. Осталось {remaining} попыток.";
            if (!JacRedLogSettings.TracksConsoleDetail)
            {
                var body = $"[task:{typetask}] hash={infohash} code={apiStatusCode} remaining={remaining} msg={TracksFailureMsgKey(errorMessage, apiStatusCode)}";
                JacRedLog.Write(JacRedLogCategories.Tracks, LogLevel.Warning, body);
                if (AppInit.conf?.trackslog == true)
                    TracksDB.LogToFile(detail, typetask);
                return;
            }

            TracksDB.Log(detail, typetask);
        }

        static string TracksFailureMsgKey(string errorMessage, int apiStatusCode)
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                if (errorMessage.Contains("Нет данных", StringComparison.Ordinal)) return "no_track_data";
                if (errorMessage.Contains("таймаут", StringComparison.OrdinalIgnoreCase)) return "timeout";
                if (errorMessage.Contains("JSON", StringComparison.Ordinal)) return "json_error";
            }

            return apiStatusCode switch
            {
                400 => "no_track_data",
                408 => "timeout",
                _ => "error"
            };
        }

        internal static HashSet<string> Languages(TorrentDetails t, List<ffStream> streams)
        {
            try
            {
                var languages = new HashSet<string>();

                if (t.languages != null)
                {
                    foreach (var l in t.languages)
                        languages.Add(l);
                }

                if (streams != null)
                {
                    foreach (var item in streams)
                    {
                        if (!string.IsNullOrEmpty(item.tags?.language) && item.codec_type == "audio")
                            languages.Add(item.tags.language);
                    }
                }

                if (languages.Count == 0)
                    return null;

                return languages;
            }
            catch { return null; }
        }
    }
}
