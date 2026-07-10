using JacRed.Models.Tracks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Tracks
{
    internal static partial class TracksAnalyzer
    {
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
    }
}
