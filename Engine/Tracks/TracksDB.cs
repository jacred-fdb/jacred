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
                        string infohash = InfohashFromTrackRelPath(
                            Path.GetFileName(folder1),
                            Path.GetFileName(folder2),
                            Path.GetFileName(file));

                        if (!IsValidInfohash(infohash))
                            continue;

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
            infohash = NormalizeInfohash(infohash);
            string folder = $"Data/tracks/{infohash.Substring(0, 2)}/{infohash[2]}";

            if (createfolder)
                Directory.CreateDirectory(folder);

            return $"{folder}/{infohash.Substring(3)}.json";
        }

        static string LegacyPathDb(string infohash)
        {
            infohash = NormalizeInfohash(infohash);
            return $"Data/tracks/{infohash.Substring(0, 2)}/{infohash[2]}/{infohash.Substring(3)}";
        }

        static string TrackFilePath(string tracksDir, string infohash, bool withExtension = true)
        {
            infohash = NormalizeInfohash(infohash);
            string folder = Path.Combine(tracksDir, infohash.Substring(0, 2), infohash[2].ToString());
            string filename = withExtension ? $"{infohash.Substring(3)}.json" : infohash.Substring(3);
            return Path.Combine(folder, filename);
        }

        static string ResolveTrackPath(string infohash)
        {
            string jsonPath = pathDb(infohash);
            if (File.Exists(jsonPath))
                return jsonPath;

            string legacyPath = LegacyPathDb(infohash);
            if (File.Exists(legacyPath))
                return legacyPath;

            return null;
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

            string path = ResolveTrackPath(infohash);
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
            return res.streams;
        }

        /// <summary>
        /// Выбирает сервер с наименьшим количеством торрентов в указанной категории
        /// </summary>
        private static async Task<string> SelectBestServer(CancellationToken token)
        {
            var servers = AppInit.conf.tsuri;
            if (servers == null || servers.Length == 0)
                return null;

            //if (servers.Length == 1)
            //	return servers[0];

            string expectedCategory = AppInit.conf.trackscategory;
            var serverTasks = new List<Task<(string server, int count, bool isValid)>>();

            foreach (var server in servers)
            {
                serverTasks.Add(GetServerTorrentCount(server, expectedCategory, token));
            }

            var results = await Task.WhenAll(serverTasks);

            // Фильтруем только валидные серверы (без ошибок)
            var validServers = results.Where(r => r.isValid).ToList();

            if (validServers.Count == 0)
            {
                //Log("Все серверы недоступны");
                return null;
            }

            // Сортируем по количеству торрентов и выбираем с наименьшим
            var bestServer = validServers.OrderBy(r => r.count).First();

            //Log($"Выбран сервер {bestServer.server} с {bestServer.count} торрентами в категории '{expectedCategory}'");
            return bestServer.server;
        }

        /// <summary>
        /// Получает количество торрентов на сервере в указанной категории
        /// </summary>
        private static async Task<(string server, int count, bool isValid)> GetServerTorrentCount(string server, string category, CancellationToken token)
        {
            try
            {
                // Используем существующую процедуру для получения списка торрентов
                (bool exists, string actualCategory, bool serverError) = await CheckTorrentExistsWithCategory(server, null, token);

                if (serverError)
                {
                    //Log($"Сервер {MaskPasswordInUrl(server)} недоступен, исключаем из выбора");
                    return (server, 0, false);
                }

                // Здесь нужно получить полный список торрентов и посчитать количество в нужной категории
                int count = await GetTorrentCountByCategory(server, category, token);

                return (server, count, true);
            }
            //catch (Exception ex)
            catch (Exception)
            {
                //Log($"Ошибка при получении информации с сервера {MaskPasswordInUrl(server)}: {ex.Message}");
                return (server, 0, false);
            }
        }

        /// <summary>
        /// Получает количество торрентов в указанной категории
        /// </summary>
        private static async Task<int> GetTorrentCountByCategory(string tsuri, string category, CancellationToken token)
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
                    //Log($"Сервер вернул ошибку при запросе списка торрентов: {(int)response.StatusCode}");
                    return 0;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(token);
                var torrents = JsonConvert.DeserializeObject<List<TorrentInfo>>(jsonResponse);

                if (torrents == null || torrents.Count == 0)
                    return 0;

                // Считаем количество торрентов в указанной категории
                return torrents.Count(t =>
                    !string.IsNullOrEmpty(t.category) &&
                    t.category.Equals(category, StringComparison.OrdinalIgnoreCase));
            }
            //catch (Exception ex)
            catch (Exception)
            {
                //Log($"Ошибка при получении количества торрентов: {ex.Message}");
                return 0;
            }
        }



        /// <summary>
        /// Анализ медиа-треков торрента
        /// </summary>
        /// <param name="magnet">Magnet-ссылка торрента</param>
        /// <param name="currentAttempt">Текущая попытка анализа</param>
        /// <param name="types">Типы контента</param>
        /// <param name="torrentKey">Ключ торрента в FileDB (search_name:search_originalname)</param>
        public static async Task Add(string magnet, int currentAttempt, string[] types = null, string torrentKey = null, int typetask = 1)
        {
            // 1. Валидация входных параметров
            if (string.IsNullOrWhiteSpace(magnet))
            {
                Log("Ошибка: magnet-ссылка не может быть пустой", typetask);
                return;
            }

            if (types != null && theBad(types))
            {
                string msg = $"Пропуск добавления треков: недопустимый тип контента [{string.Join(", ", types)}]";
                Log(msg, typetask);
                return;
            }

            if (AppInit.conf?.tsuri == null || AppInit.conf.tsuri.Length == 0)
            {
                Log("Ошибка: не настроены tsuri серверы", typetask);
                return;
            }

            // Проверяем наличие категории в конфигурации
            if (string.IsNullOrEmpty(AppInit.conf.trackscategory))
            {
                Log("Ошибка: не настроена trackscategory", typetask);
                return;
            }

            // 2. Извлечение инфохаша
            string infohash;
            try
            {
                infohash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
                if (string.IsNullOrEmpty(infohash))
                {
                    Log("Ошибка: не удалось извлечь infohash из magnet-ссылки", typetask);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка парсинга magnet-ссылки: {ex.Message}", typetask);
                return;
            }

            // 3. Логирование начала операции
            Log($"Начало анализа треков для {infohash}.", typetask);

            FfprobeModel res = null;

            //string tsuri = AppInit.conf.tsuri[random.Next(0, AppInit.conf.tsuri.Length)];
            string tsuri;
            //Log("Выбираем сервер", typetask);
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(1));
                var token = cancellationTokenSource.Token;

                tsuri = await SelectBestServer(token);
                if (string.IsNullOrEmpty(tsuri))
                {
                    Log("Все серверы недоступны. Пауза 1 минута...");
                    await Task.Delay(TimeSpan.FromMinutes(1), token);
                    Log("Пауза завершена. Выход.");
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

                    // 4. Пытаемся добавить торрент на сервер
                    (bool torrentAdded, bool torrentExistsInCorrectCategory, bool serverError) =
                        await AddTorrentToServer(tsuri, magnet, infohash, expectedCategory, token, typetask);

                    if (serverError)
                    {
                        errorMessage = "Сервер вернул ошибку при получении списка торрентов";
                        Log($"{errorMessage}. Пауза 1 минута...", typetask);

                        // Держим паузу 1 минуту
                        await Task.Delay(TimeSpan.FromMinutes(1), token);

                        Log("Пауза завершена. Выход.", typetask);
                        return;
                    }

                    bool shouldAnalyze = torrentAdded || torrentExistsInCorrectCategory;

                    if (!shouldAnalyze)
                    {
                        if (torrentExistsInCorrectCategory == false)
                        {
                            errorMessage = $"Торрент не в категории '{expectedCategory}'";
                            Log($"{errorMessage}. Анализ отменен.", typetask);
                        }
                        else
                        {
                            errorMessage = "Не удалось добавить торрент на сервер";
                            Log($"{errorMessage} и он не существует в категории '{expectedCategory}'. Завершение.", typetask);
                        }
                        return;
                    }

                    if (torrentExistsInCorrectCategory)
                    {
                        Log($"Торрент {infohash} уже существует на сервере в категории '{expectedCategory}'. Начинаем анализ...", typetask);
                    }
                    else if (torrentAdded)
                    {
                        Log($"Торрент {infohash} успешно добавлен в категорию '{expectedCategory}'. Начинаем анализ...", typetask);
                    }

                    // 5. Небольшая пауза для инициализации торрента
                    if (torrentAdded)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), token);
                    }

                    // 6. Вызов внешнего API для анализа
                    (res, apiStatusCode) = await AnalyzeWithExternalApi(tsuri, infohash, token, typetask);

                    if (res?.streams != null && res.streams.Count > 0)
                    {
                        analysisSuccessful = true;
                        Log($"API успешно вернул {res.streams.Count} треков", typetask);
                    }
                    else
                    {
                        errorMessage = "Нет данных о треках";
                        Log($"{errorMessage} для инфохаша {infohash} (код: {apiStatusCode})", typetask);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                errorMessage = $"Анализ для инфохаша {infohash} отменен по таймауту (3 минуты)";
                Log(errorMessage, typetask);
                apiStatusCode = 408;
            }
            catch (JsonException ex)
            {
                errorMessage = $"Ошибка обработки JSON ответа: {ex.Message}";
                Log(errorMessage, typetask);
            }
            catch (Exception ex)
            {
                errorMessage = $"Критическая ошибка при анализе треков: {ex.Message}";
                Log(errorMessage, typetask);
                LogToFile($"StackTrace: {ex.StackTrace}", typetask);
            }
            finally
            {
                // 7. Очистка торрента на сервере
                await CleanupTorrent(tsuri, infohash, expectedCategory, typetask);
            }

            // 8. Обновление данных в базе
            await UpdateAnalysisResults(magnet, torrentKey, infohash, currentAttempt, analysisSuccessful, res, typetask, apiStatusCode, errorMessage);
        }

        /// <summary>
        /// Обновляет результаты анализа в базе данных
        /// </summary>
        private static async Task UpdateAnalysisResults(string magnet, string torrentKey, string infohash,
            int currentAttempt, bool analysisSuccessful, FfprobeModel ffprobeResult, int typetask, int apiStatusCode, string errorMessage = null)
        {
            try
            {
                if (string.IsNullOrEmpty(torrentKey))
                {
                    // Пытаемся найти ключ по magnet/инфохашу
                    torrentKey = FindTorrentKeyByMagnet(magnet);
                    if (string.IsNullOrEmpty(torrentKey))
                    {
                        Log($"Не удалось найти torrentKey для {infohash}. Обновление ffprobe_tryingdata невозможно.", typetask);
                        return;
                    }
                }

                if (analysisSuccessful)
                {
                    // Анализ успешен - сбрасываем счетчик и сохраняем результаты
                    // FileDB.UpdateTorrentFfprobeInfo(torrentKey, magnet, 0, ffprobeResult);

                    // Сохраняем результаты в tracks базу
                    if (ffprobeResult?.streams != null && ffprobeResult.streams.Count > 0)
                    {
                        await SaveTrackResults(ffprobeResult, infohash, typetask);
                    }

                    Log($"Анализ треков для {infohash} успешно завершен!", typetask);
                }
                else
                {
                    // Анализ неуспешен - увеличиваем счетчик попыток
                    int NewAttepmt = currentAttempt;

                    if (typetask != 1)
                    {
                        NewAttepmt++;

                        if (apiStatusCode == 400)
                            NewAttepmt = AppInit.conf.tracksatempt;
                    }

                    if (NewAttepmt != currentAttempt)
                        FileDB.UpdateTorrentFfprobeInfo(torrentKey, magnet, NewAttepmt);

                    Log($"Анализ треков для {infohash} без результата. Код ответа API: {apiStatusCode}. Осталось {AppInit.conf.tracksatempt - NewAttepmt} попыток.", typetask);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при обновлении результатов анализа: {ex.Message}", typetask);
            }
        }

        /// <summary>
        /// Находит ключ торрента в FileDB по magnet-ссылке
        /// </summary>
        private static string FindTorrentKeyByMagnet(string magnet)
        {
            try
            {
                var infohash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
                if (string.IsNullOrEmpty(infohash))
                    return null;

                // Проверяем все ключи в masterDb
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
        /// Проверяет существование торрента на сервере и его категорию
        /// Возвращает кортеж: (существует ли торрент, категория торрента, была ли ошибка сервера)
        /// </summary>
        private static async Task<(bool exists, string category, bool serverError)> CheckTorrentExistsWithCategory(
            string tsuri, string infohash, CancellationToken token, int? typetask = null)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                // Добавляем Basic Authentication заголовок
                AddBasicAuthHeader(client, tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "list"
                });

                var content = new System.Net.Http.StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await client.PostAsync($"{tsuri}/torrents", content, token);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"Сервер вернул ошибку при запросе списка торрентов: {(int)response.StatusCode}");
                    // Возвращаем флаг ошибки сервера
                    return (false, null, true);
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(token);

                // Десериализуем ответ
                var torrents = JsonConvert.DeserializeObject<List<TorrentInfo>>(jsonResponse);

                if (torrents == null)
                {
                    Log("Получен пустой список торрентов");
                    return (false, null, false); // Нет ошибки, но и торрента нет
                }

                // Если инфохаш не указан, возвращаем только информацию об ошибке/доступности
                if (string.IsNullOrEmpty(infohash))
                {
                    return (false, null, false);
                }

                // Ищем торрент по инфохашу
                var torrent = torrents.FirstOrDefault(t =>
                    (!string.IsNullOrEmpty(t.hash) &&
                     t.hash.Equals(infohash, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(t.name) &&
                     t.name.EndsWith(infohash, StringComparison.OrdinalIgnoreCase)));

                if (torrent == null)
                {
                    return (false, null, false); // Торрент не найден
                }

                // Всегда возвращаем категорию (даже если null или пустая)
                string torrentCategory = torrent.category ?? string.Empty;

                return (true, torrentCategory, false); // Найден, возвращаем категорию
            }
            catch (TaskCanceledException)
            {
                //Log("Таймаут при проверке существования торрента");
                return (false, null, true); // Таймаут считаем ошибкой сервера
            }
            catch (Exception)
            {
                //Log($"Ошибка при проверке существования торрента: {ex.Message}");
                return (false, null, true); // Любая другая ошибка - ошибка сервера
            }
        }

        /// <summary>
        /// Класс для десериализации информации о торренте
        /// </summary>
        public class TorrentInfo
        {
            public string title { get; set; }
            public string category { get; set; }
            public string poster { get; set; }
            public long timestamp { get; set; }
            public string name { get; set; }
            public string hash { get; set; }
            public int stat { get; set; }
            public string stat_string { get; set; }
        }

        /// <summary>
        /// Добавляет торрент на сервер с потоковым чтением
        /// Возвращает: (успешно ли добавлен, существует ли уже торрент в правильной категории, была ли ошибка сервера)
        /// </summary>
        private static async Task<(bool added, bool existsInCorrectCategory, bool serverError)> AddTorrentToServer(
            string tsuri, string magnet, string infohash, string expectedCategory, CancellationToken token, int? typetask = null)
        {
            try
            {
                // Проверяем существование и категорию торрента
                (bool exists, string actualCategory, bool serverError) = await CheckTorrentExistsWithCategory(tsuri, infohash, token, typetask);

                if (serverError)
                {
                    return (false, false, true); // Не добавляем при ошибке сервера
                }

                if (exists)
                {
                    // Проверяем категорию существующего торрента
                    bool isCorrectCategory = actualCategory?.Equals(expectedCategory, StringComparison.OrdinalIgnoreCase) ?? false;

                    if (isCorrectCategory)
                    {
                        // Торрент существует в правильной категории - не добавляем, но будем анализировать
                        return (false, true, false); // Существует в правильной категории
                    }
                    else
                    {
                        // Торрент существует, но в другой категории - не добавляем и не анализируем
                        return (false, false, false); // Существует, но не в правильной категории
                    }
                }

                // Торрента нет - добавляем с указанной категорией
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                // Добавляем Basic Authentication заголовок
                AddBasicAuthHeader(client, tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "add",
                    link = magnet,
                    save_to_db = false,
                    category = expectedCategory // Используем категорию из конфигурации
                });

                var content = new System.Net.Http.StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await client.PostAsync($"{tsuri}/torrents", content, token);

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
                    }

                    return (true, false, false); // Успешно добавлен
                }
                else
                {
                    Log($"Ошибка при добавлении торрента ({(int)response.StatusCode})", typetask);
                    return (false, false, false); // Не удалось добавить
                }
            }
            catch (TaskCanceledException)
            {
                Log("Таймаут при добавлении торрента на сервер", typetask);
                return (false, false, true);
            }
            catch (Exception ex)
            {
                Log($"Ошибка при добавлении торрента на сервере: {ex.Message}", typetask);
                return (false, false, true);
            }
        }

        /// <summary>
        /// Вызов внешнего API для анализа медиа-файла
        /// </summary>
        private static async Task<(FfprobeModel result, int statusCode)> AnalyzeWithExternalApi(
            string tsuri, string infohash, CancellationToken token, int? typetask = null)
        {
            string apiUrl = $"{tsuri}/ffp/{infohash.ToUpper()}/1";

            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromMinutes(2);

            // Добавляем Basic Authentication заголовок
            AddBasicAuthHeader(client, tsuri);

            // Для API тоже используем потоковое чтение
            using var response = await client.GetAsync(apiUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);

            int statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                // Возвращаем результат null и код статуса
                return (null, statusCode);
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
                return (null, statusCode);
            }

            var result = JsonConvert.DeserializeObject<FfprobeModel>(jsonResponse);

            if (result == null)
            {
                return (null, statusCode);
            }

            return (result, statusCode);
        }

        /// <summary>
        /// Очистка торрента на сервере (ВСЕГДА выполняется, но только если торрент в указанной категории)
        /// Удаление производится только по хешу
        /// </summary>
        private static async Task CleanupTorrent(string tsuri, string infohash, string expectedCategory, int? typetask = null)
        {
            try
            {
                // Проверяем существование и категорию торрента
                (bool exists, string actualCategory, bool serverError) = await CheckTorrentExistsWithCategory(tsuri, infohash, CancellationToken.None, typetask);

                if (serverError)
                {
                    Log($"Сервер вернул ошибку при запросе списка торрентов. Удаление отменено.", typetask);
                    return; // Не удаляем при ошибке сервера
                }

                if (!exists)
                {
                    Log($"Торрент {infohash} не найден на сервере. Удаление не требуется.", typetask);
                    return; // Торрента нет - нечего удалять
                }

                // Проверяем категорию для удаления
                bool isExpectedCategory = actualCategory?.Equals(expectedCategory, StringComparison.OrdinalIgnoreCase) ?? false;

                if (!isExpectedCategory)
                {
                    Log($"Торрент {infohash} не в категории '{expectedCategory}' (категория: '{actualCategory}'). Удаление отменено.", typetask);
                    return; // Не удаляем торренты из других категорий
                }

                // Торрент существует, в правильной категории и сервер работает - выполняем удаление
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                // Добавляем Basic Authentication заголовок
                AddBasicAuthHeader(client, tsuri);

                // Удаление производится только по хешу, без указания категории
                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "rem",
                    hash = infohash
                });

                var content = new System.Net.Http.StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await client.PostAsync($"{tsuri}/torrents", content);

                if (response.IsSuccessStatusCode)
                {
                    Log($"Торрент {infohash} успешно удален с сервера", typetask);
                }
                else
                {
                    Log($"Ошибка при удалении торрента ({(int)response.StatusCode})", typetask);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при очистке торрента {infohash} на сервере: {ex.Message}", typetask);
            }
        }

        /// <summary>
        /// Сохраняет результаты анализа треков
        /// </summary>
        private static async Task SaveTrackResults(FfprobeModel result, string infohash, int? typetask = null)
        {
            if (result?.streams == null || result.streams.Count == 0)
                return;

            int audioCount = result.streams.Count(s => s.codec_type == "audio");
            int videoCount = result.streams.Count(s => s.codec_type == "video");

            Log($"Сохранение данных треков для {infohash}. Аудио: {audioCount}, видео: {videoCount}", typetask);

            // Сохранение в памяти
            try
            {
                Database.AddOrUpdate(infohash, result, (k, v) => result);
            }
            catch (Exception ex)
            {
                Log($"Ошибка при обновлении данных в памяти: {ex.Message}", typetask);
            }

            // Сохранение в файл (.json — формат lampa-tracks / R2)
            try
            {
                string path = pathDb(infohash, createfolder: true);
                string json = JsonConvert.SerializeObject(result, Formatting.Indented);
                await File.WriteAllTextAsync(path, json, Encoding.UTF8);

                string legacyPath = LegacyPathDb(infohash);
                if (File.Exists(legacyPath) && !string.Equals(path, legacyPath, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(legacyPath); }
                    catch { }
                }

                // Логирование информации о языках
                var audioLanguages = result.streams
                    .Where(s => s.codec_type == "audio" && s.tags?.language != null)
                    .Select(s => s.tags.language)
                    .Distinct()
                    .ToList();

                if (audioLanguages.Any())
                {
                    Log($"Обнаружены аудио дорожки на языках: {string.Join(", ", audioLanguages)}", typetask);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при сохранении данных в файл: {ex.Message}", typetask);
                LogToFile($"StackTrace: {ex.StackTrace}", typetask);
            }
        }

        /// <summary>
        /// Логирование в консоль и файл
        /// </summary>
        public static void Log(string message, int? typetask = null)
        {
            string timeNow = DateTime.Now.ToString("HH:mm:ss");
            string typetaskInfo = typetask.HasValue ? $" [task:{typetask.Value}]" : "";
            string fullMessage = $"tracks: [{timeNow}]{typetaskInfo} {message}";

            Console.WriteLine(fullMessage);

            if (AppInit.conf?.trackslog == true)
            {
                LogToFile(message, typetask);
            }
        }

        /// <summary>
        /// Логирование в файл
        /// </summary>
        public static void LogToFile(string message, int? typetask = null)
        {
            try
            {
                string logDir = "Data/log";
                string logFile = Path.Combine(logDir, "tracks.log");

                Directory.CreateDirectory(logDir);

                string timeNow = DateTime.Now.ToString("HH:mm:ss");
                string typetaskInfo = typetask.HasValue ? $" [task:{typetask.Value}]" : "";
                string logMessage = $"tracks: [{timeNow}]{typetaskInfo} {message}{Environment.NewLine}";

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

        static bool IsValidInfohash(string infohash) =>
            !string.IsNullOrEmpty(infohash) && infohash.Length == 40 && infohash.All(c =>
                (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

        static string NormalizeInfohash(string infohash) => infohash?.ToLowerInvariant();

        static string InfohashFromTrackRelPath(string prefix2, string prefix1, string filename)
        {
            var stem = filename;
            if (stem.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                stem = stem.Substring(0, stem.Length - 5);

            return NormalizeInfohash(prefix2 + prefix1 + stem);
        }

        /// <summary>
        /// R2 / lampa-tracks layout: {HASH[0:2]}/{HASH[2]}/{HASH[3:40]}.json — uppercase hex.
        /// </summary>
        static string ExportFilePath(string outputDir, string infohash)
        {
            infohash = NormalizeInfohash(infohash);
            var upper = infohash.ToUpperInvariant();
            string folder = Path.Combine(outputDir, upper.Substring(0, 2), upper.Substring(2, 1));
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, $"{upper.Substring(3)}.json");
        }

        /// <summary>
        /// Сканирует Data/tracks и (опционально) ffprobe в FileDB. Файлы tracks имеют приоритет над полем torrent.ffprobe.
        /// </summary>
        public static Dictionary<string, FfprobeModel> CollectAll(bool includeTorrentDb = true)
        {
            var result = new Dictionary<string, FfprobeModel>(StringComparer.OrdinalIgnoreCase);
            CollectAllInto(result, null, includeTorrentDb);
            return result;
        }

        static void CollectAllInto(Dictionary<string, FfprobeModel> result, TracksExportStats stats, bool includeTorrentDb)
        {
            CollectFromTracksDir("Data/tracks", result, stats);

            foreach (var item in Database)
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

                        string infohash = InfohashFromTrackRelPath(
                            Path.GetFileName(folder1),
                            Path.GetFileName(folder2),
                            Path.GetFileName(file));

                        if (!IsValidInfohash(infohash))
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
                        infohash = NormalizeInfohash(MagnetLink.Parse(torrent.magnet).InfoHashes.V1OrV2.ToHex());
                    }
                    catch
                    {
                        if (stats != null)
                            stats.magnetErrors++;
                        continue;
                    }

                    if (!IsValidInfohash(infohash))
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

        public static TracksExportStats GetExportStats(bool includeTorrentDb = true)
        {
            var result = new Dictionary<string, FfprobeModel>(StringComparer.OrdinalIgnoreCase);
            var stats = new TracksExportStats();
            CollectAllInto(result, stats, includeTorrentDb);
            stats.total = result.Count;
            return stats;
        }

        static volatile bool _exportRunning;
        static readonly object _exportLock = new object();
        static TracksExportJobStatus _exportJob = new TracksExportJobStatus();

        public static TracksExportJobStatus GetExportJobStatus()
        {
            lock (_exportLock)
                return _exportJob.Clone();
        }

        /// <summary>
        /// Запускает ExportAll в фоне. Возвращает false, если экспорт уже идёт.
        /// </summary>
        public static bool TryStartExport(string outputDir = "Data/tracks-export", bool includeTorrentDb = true)
        {
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

                    Console.WriteLine($"tracks export: {ex}");
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
        public static TracksExportResult ExportAll(string outputDir = "Data/tracks-export", bool dryRun = false, bool includeTorrentDb = true, TracksExportJobStatus progress = null)
        {
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

            var data = new Dictionary<string, FfprobeModel>(StringComparer.OrdinalIgnoreCase);
            result.stats = new TracksExportStats();
            CollectAllInto(data, result.stats, includeTorrentDb);
            result.stats.total = data.Count;

            if (progress != null)
            {
                progress.phase = dryRun ? "done" : "writing";
                progress.total = data.Count;
                progress.stats = result.stats;
            }

            if (dryRun)
                return result;

            Directory.CreateDirectory(outputDir);

            foreach (var item in data)
            {
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
        public static TracksBackfillResult BackfillTracks(string tracksDir = "Data/tracks", bool dryRun = false, bool includeTorrentDb = true, bool migrateLegacy = true)
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

            if (dryRun)
            {
                foreach (var item in data)
                {
                    string jsonPath = TrackFilePath(tracksDir, item.Key, withExtension: true);
                    string legacyPath = TrackFilePath(tracksDir, item.Key, withExtension: false);

                    if (File.Exists(jsonPath))
                        result.skippedExisting++;
                    else if (migrateLegacy && File.Exists(legacyPath))
                        result.migratedLegacy++;
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
                    string jsonPath = TrackFilePath(tracksDir, item.Key, withExtension: true);
                    string legacyPath = TrackFilePath(tracksDir, item.Key, withExtension: false);
                    Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));

                    if (File.Exists(jsonPath))
                    {
                        result.skippedExisting++;
                        if (migrateLegacy && File.Exists(legacyPath))
                        {
                            try { File.Delete(legacyPath); result.migratedLegacy++; }
                            catch { }
                        }
                        continue;
                    }

                    if (migrateLegacy && File.Exists(legacyPath))
                    {
                        File.Move(legacyPath, jsonPath);
                        result.migratedLegacy++;
                        Database.AddOrUpdate(item.Key, item.Value, (k, v) => item.Value);
                        continue;
                    }

                    string json = JsonConvert.SerializeObject(item.Value, Formatting.Indented);
                    File.WriteAllText(jsonPath, json, Encoding.UTF8);
                    Database.AddOrUpdate(item.Key, item.Value, (k, v) => item.Value);
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

    public class TracksExportStats
    {
        public int total { get; set; }
        public int filesScanned { get; set; }
        public int fromTracksFiles { get; set; }
        public int fromMemory { get; set; }
        public int fromTorrentDb { get; set; }
        public int torrentsScanned { get; set; }
        public int invalidPath { get; set; }
        public int emptyStreams { get; set; }
        public int readErrors { get; set; }
        public int magnetErrors { get; set; }
        public int torrentDbErrors { get; set; }
    }

    public class TracksExportResult
    {
        public string outputDir { get; set; }
        public bool dryRun { get; set; }
        public bool includeTorrentDb { get; set; }
        public TracksExportStats stats { get; set; }
        public int written { get; set; }
        public int writeErrors { get; set; }
        public List<object> errorSamples { get; set; } = new List<object>();
    }

    public class TracksExportJobStatus
    {
        public bool running { get; set; }
        public string phase { get; set; }
        public string outputDir { get; set; }
        public bool includeTorrentDb { get; set; }
        public DateTime? startedAt { get; set; }
        public DateTime? completedAt { get; set; }
        public int total { get; set; }
        public int written { get; set; }
        public int writeErrors { get; set; }
        public TracksExportStats stats { get; set; }
        public TracksExportResult result { get; set; }
        public string error { get; set; }

        public TracksExportJobStatus Clone()
        {
            return new TracksExportJobStatus
            {
                running = running,
                phase = phase,
                outputDir = outputDir,
                includeTorrentDb = includeTorrentDb,
                startedAt = startedAt,
                completedAt = completedAt,
                total = total,
                written = written,
                writeErrors = writeErrors,
                stats = stats,
                result = result,
                error = error
            };
        }
    }

    public class TracksBackfillResult
    {
        public string tracksDir { get; set; }
        public bool dryRun { get; set; }
        public bool includeTorrentDb { get; set; }
        public bool migrateLegacy { get; set; }
        public TracksExportStats stats { get; set; }
        public int written { get; set; }
        public int migratedLegacy { get; set; }
        public int skippedExisting { get; set; }
        public int writeErrors { get; set; }
        public List<object> errorSamples { get; set; } = new List<object>();
    }
}