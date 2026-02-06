using JacRed.Engine.CORE;
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
using System.Web;




namespace JacRed.Engine
{
    public static class TracksDB
    {
        public static void Configuration()
        {
            Console.WriteLine("TracksDB load");

            foreach (var folder1 in Directory.GetDirectories("Data/tracks"))
            {
                foreach (var folder2 in Directory.GetDirectories(folder1))
                {
                    foreach (var file in Directory.GetFiles(folder2))
                    {
                        string infohash = folder1.Substring(12) + folder2.Substring(folder1.Length + 1) + Path.GetFileName(file);

                        try
                        {
                            var res = JsonConvert.DeserializeObject<FfprobeModel>(File.ReadAllText(file));
                            if (res?.streams != null && res.streams.Count > 0)
                                Database.TryAdd(infohash, res);
                        }
                        catch { }
                    }
                }
            }
        }

        static Random random = new Random();

        static ConcurrentDictionary<string, FfprobeModel> Database = new ConcurrentDictionary<string, FfprobeModel>();

        static string pathDb(string infohash, bool createfolder = false)
        {
            string folder = $"Data/tracks/{infohash.Substring(0, 2)}/{infohash[2]}";

            if (createfolder)
                Directory.CreateDirectory(folder);

            return $"{folder}/{infohash.Substring(3)}";
        }

        public static bool theBad(string[] types)
        {
            if (types == null || types.Length == 0)
                return true;

            if (types.Contains("sport") || types.Contains("tvshow") || types.Contains("docuserial"))
                return true;

            return false;
        }

        public static List<ffStream> Get(string magnet, string[] types = null, bool onlydb = false)
        {
            if (types != null && theBad(types))
                return null;

            string infohash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
            if (Database.TryGetValue(infohash, out FfprobeModel res))
                return res.streams;

            string path = pathDb(infohash);
            if (!File.Exists(path))
                return null;

            try
            {
                res = JsonConvert.DeserializeObject<FfprobeModel>(File.ReadAllText(path));
                if (res?.streams == null || res.streams.Count == 0)
                    return null;
            }
            catch { return null; }

            Database.AddOrUpdate(infohash, res, (k, v) => res);
            return res.streams;
        }



        ///Fix by lexandr0s

        public static async Task Add(string magnet, string[] types = null)
        {
            // 1. Валидация входных параметров
            if (string.IsNullOrWhiteSpace(magnet))
            {
                Log("Ошибка: magnet-ссылка не может быть пустой");
                return;
            }

            if (types != null && theBad(types))
            {
                string msg = $"Пропуск добавления треков: недопустимый тип контента [{string.Join(", ", types)}]";
                Log(msg);
                return;
            }

            if (AppInit.conf?.tsuri == null || AppInit.conf.tsuri.Length == 0)
            {
                Log("Ошибка: не настроены tsuri серверы");
                return;
            }

            // 2. Извлечение инфохаша
            string infohash;
            try
            {
                infohash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
                if (string.IsNullOrEmpty(infohash))
                {
                    Log("Ошибка: не удалось извлечь infohash из magnet-ссылки");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка парсинга magnet-ссылки: {ex.Message}");
                return;
            }

            // 3. Логирование начала операции
            Log($"Начало анализа треков для инфохаша: {infohash}");

            FfprobeModel res = null;
            string tsuri = AppInit.conf.tsuri[random.Next(0, AppInit.conf.tsuri.Length)];

            try
            {
                using (var cancellationTokenSource = new CancellationTokenSource())
                {
                    cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(3));
                    var token = cancellationTokenSource.Token;

                    // 4. Сначала добавляем торрент на сервер
                    bool torrentAdded = await AddTorrentToServer(tsuri, magnet, infohash, token);

                    if (!torrentAdded)
                    {
                        Log($"Не удалось добавить торрент {infohash} на сервер. Завершение.");
                        // Очистка всё равно будет выполнена
                    }
                    else
                    {
                        // 5. Небольшая пауза для инициализации торрента
                        await Task.Delay(TimeSpan.FromSeconds(3), token);

                        // 6. Вызов внешнего API для анализа
                        res = await AnalyzeWithExternalApi(tsuri, infohash, token);

                        if (res?.streams == null || res.streams.Count == 0)
                        {
                            Log($"Нет данных о треков для инфохаша {infohash}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log("Анализ треков отменен по таймауту (3 минуты)");
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                Log($"Ошибка HTTP при анализе треков: {ex.Message}");
            }
            catch (JsonException ex)
            {
                Log($"Ошибка обработки JSON ответа: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"Критическая ошибка при анализе треков: {ex.Message}");
                LogToFile($"StackTrace: {ex.StackTrace}");
            }
            finally
            {
                // 7. Очистка торрента на сервере (ВСЕГДА)
                await CleanupTorrent(tsuri, infohash);
            }

            // 8. Сохранение результатов (только если анализ успешен)
            if (res?.streams != null && res.streams.Count > 0)
            {
                await SaveTrackResults(res, infohash);
                Log($"Анализ треков для {infohash} успешно завершен!");
            }
            else
            {
                Log($"Анализ треков для {infohash} завершен (без результатов)");
            }
        }

        /// <summary>
        /// Добавляет Basic Authentication заголовок в HttpClient
        /// </summary>
        private static void AddBasicAuthHeader(System.Net.Http.HttpClient client, string url)
        {
            try
            {
                var uri = new Uri(url);
                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    // Разделяем логин:пароль
                    var credentials = uri.UserInfo.Split(':');
                    if (credentials.Length == 2)
                    {
                        string username = credentials[0];
                        string password = credentials[1];

                        // Создаем Basic Auth заголовок
                        var byteArray = Encoding.ASCII.GetBytes($"{username}:{password}");
                        var base64String = Convert.ToBase64String(byteArray);
                        client.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64String);

                        // Также добавляем заголовок Accept для JSON
                        client.DefaultRequestHeaders.Accept.Clear();
                        client.DefaultRequestHeaders.Accept.Add(
                            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    }
                }
            }
            catch (Exception ex)
            {
                // Если не удалось распарсить URL, логируем ошибку
                Log($"Ошибка при добавлении Basic Auth: {ex.Message}");
            }
        }

        /// <summary>
        /// Маскирует пароль в URL для безопасного логирования
        /// </summary>
        private static string MaskPasswordInUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    var credentials = uri.UserInfo.Split(':');
                    if (credentials.Length == 2)
                    {
                        // Маскируем пароль, но оставляем логин
                        string maskedUrl = url.Replace(
                            $"{credentials[0]}:{credentials[1]}",
                            $"{credentials[0]}:***");
                        return maskedUrl;
                    }
                }
            }
            catch
            {
                // В случае ошибки возвращаем оригинальный URL
            }
            return url;
        }

        /// <summary>
        /// Добавляет торрент на сервер с потоковым чтением
        /// </summary>
        private static async Task<bool> AddTorrentToServer(string tsuri, string magnet, string infohash, CancellationToken token)
        {
            try
            {
                Log($"Добавление торрента {infohash} на сервер...");

                //Используем простое добавление торрента, вместо вызова плеера

                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                // Добавляем Basic Authentication заголовок
                AddBasicAuthHeader(client, tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "add",
                    link = magnet
                });

                var content = new System.Net.Http.StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Для POST запроса также можно использовать потоковое чтение если ожидается большой ответ
                using var response = await client.PostAsync($"{tsuri}/torrents", content);


                if (response.IsSuccessStatusCode)
                {
                    // Читаем ответ потоком порциями
                    using var stream = await response.Content.ReadAsStreamAsync();

                    // Буфер для чтения
                    byte[] buffer = new byte[8192]; // 8KB
                    long totalBytes = 0;
                    int bytesRead;

                    // Читаем порциями, чтобы не загружать всю память
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        totalBytes += bytesRead;
                        // Просто продолжаем чтение чтобы освободить поток
                        // Если нужна обработка данных, можно добавить её здесь
                    }

                    Log($"Торрент {infohash} успешно добавлен на сервер (получено {totalBytes} байт)");
                    return true;
                }
                else
                {
                    Log($"Ошибка при добавлении торрента ({(int)response.StatusCode})");
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                Log("Таймаут при добавлении торрента на сервер");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Ошибка при добавлении торрента на сервер: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Вызов внешнего API для анализа медиа-файла
        /// </summary>
        private static async Task<FfprobeModel> AnalyzeWithExternalApi(string tsuri, string infohash, CancellationToken token)
        {
            string apiUrl = $"{tsuri}/ffp/{infohash.ToUpper()}/1";

            // Логируем с маскировкой пароля
            Log($"Вызов API анализа: {MaskPasswordInUrl(apiUrl)}");

            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromMinutes(2);

            // Добавляем Basic Authentication заголовок
            AddBasicAuthHeader(client, tsuri);

            // Для API тоже используем потоковое чтение
            using var response = await client.GetAsync(apiUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);

            if (!response.IsSuccessStatusCode)
            {
                throw new System.Net.Http.HttpRequestException($"API вернул ошибку: {(int)response.StatusCode}");
            }

            // Читаем JSON ответ потоком
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            // Используем StringBuilder для накопления данных
            var jsonBuilder = new StringBuilder();
            char[] buffer = new char[8192];
            int charsRead;

            while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                jsonBuilder.Append(buffer, 0, charsRead);
            }

            string jsonResponse = jsonBuilder.ToString();

            if (string.IsNullOrWhiteSpace(jsonResponse))
            {
                throw new InvalidDataException("API вернул пустой ответ");
            }

            var result = JsonConvert.DeserializeObject<FfprobeModel>(jsonResponse);

            if (result == null)
            {
                throw new InvalidDataException("Не удалось десериализовать ответ API");
            }

            Log($"API успешно вернул {result.streams?.Count ?? 0} треков");
            return result;
        }

        /// <summary>
        /// Очистка торрента на сервере (ВСЕГДА выполняется)
        /// </summary>
        private static async Task CleanupTorrent(string tsuri, string infohash)
        {
            try
            {
                Log($"Очистка торрента {infohash} на сервере...");

                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                // Добавляем Basic Authentication заголовок
                AddBasicAuthHeader(client, tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "rem",
                    hash = infohash
                });

                var content = new System.Net.Http.StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Для POST запроса также можно использовать потоковое чтение если ожидается большой ответ
                using var response = await client.PostAsync($"{tsuri}/torrents", content);

                if (response.IsSuccessStatusCode)
                {
                    Log($"Торрент {infohash} успешно удален с сервера");
                }
                else
                {
                    Log($"Ошибка при очистке торрента ({(int)response.StatusCode})");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при очистке торрента {infohash} на сервере: {ex.Message}");
            }
        }

        /// <summary>
        /// Сохраняет результаты анализа треков
        /// </summary>
        private static async Task SaveTrackResults(FfprobeModel result, string infohash)
        {
            if (result?.streams == null || result.streams.Count == 0)
                return;

            int audioCount = result.streams.Count(s => s.codec_type == "audio");
            int videoCount = result.streams.Count(s => s.codec_type == "video");

            Log($"Сохранение данных треков для {infohash}. Аудио: {audioCount}, видео: {videoCount}");

            // Сохранение в памяти
            try
            {
                Database.AddOrUpdate(infohash, result, (k, v) => result);
                Log($"Данные треков для {infohash} обновлены в памяти");
            }
            catch (Exception ex)
            {
                Log($"Ошибка при обновлении данных в памяти: {ex.Message}");
            }

            // Сохранение в файл
            try
            {
                string path = pathDb(infohash, createfolder: true);
                string json = JsonConvert.SerializeObject(result, Formatting.Indented);
                await File.WriteAllTextAsync(path, json, Encoding.UTF8);

                Log($"Данные треков для {infohash} сохранены в файл: {path}");

                // Логирование информации о языках
                var audioLanguages = result.streams
                    .Where(s => s.codec_type == "audio" && s.tags?.language != null)
                    .Select(s => s.tags.language)
                    .Distinct()
                    .ToList();

                if (audioLanguages.Any())
                {
                    Log($"Обнаружены аудио дорожки на языках: {string.Join(", ", audioLanguages)}");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при сохранении данных в файл: {ex.Message}");
                LogToFile($"StackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Логирование в консоль и файл
        /// </summary>
        private static void Log(string message)
        {
            string timeNow = DateTime.Now.ToString("HH:mm:ss");
            string fullMessage = $"tracks: [{timeNow}] {message}";

            Console.WriteLine(fullMessage);

            if (AppInit.conf?.trackslog == true)
            {
                LogToFile(message);
            }
        }

        /// <summary>
        /// Логирование в файл
        /// </summary>
        private static void LogToFile(string message)
        {
            try
            {
                string logDir = "Data/log";
                string logFile = Path.Combine(logDir, "tracks.log");

                Directory.CreateDirectory(logDir);

                string timeNow = DateTime.Now.ToString("HH:mm:ss");
                string logMessage = $"tracks: [{timeNow}] {message}{Environment.NewLine}";

                // Используем FileStream с FileShare.ReadWrite для избежания блокировок
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        using (var stream = new FileStream(
                            logFile,
                            FileMode.Append,
                            FileAccess.Write,
                            FileShare.ReadWrite))
                        using (var writer = new StreamWriter(stream, Encoding.UTF8))
                        {
                            writer.Write(logMessage);
                        }
                        break;
                    }
                    catch (IOException) when (i < 2)
                    {
                        Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string timeNow = DateTime.Now.ToString("HH:mm:ss");
                    Console.WriteLine($"tracks: [{timeNow}] Ошибка записи в лог файл: {ex.Message}");
                }
                catch { }
            }
        }
        ///

        public static HashSet<string> Languages(TorrentDetails t, List<ffStream> streams)
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
