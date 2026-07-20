using JacRed.Models.Tracks;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
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
        static readonly ConcurrentDictionary<string, HttpClient> _httpClients =
            new ConcurrentDictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);

        static readonly ConcurrentDictionary<string, ListCacheEntry> _listCache =
            new ConcurrentDictionary<string, ListCacheEntry>(StringComparer.OrdinalIgnoreCase);

        static readonly TimeSpan ListCacheTtl = TimeSpan.FromSeconds(10);

        static readonly object _concurrencyGate = new object();
        static SemaphoreSlim _analyzeConcurrency;
        static int _analyzeConcurrencyLimit = -1;

        static SemaphoreSlim AnalyzeConcurrency
        {
            get
            {
                int limit = Math.Max(1, AppInit.conf?.tracksconcurrency ?? 2);
                lock (_concurrencyGate)
                {
                    if (_analyzeConcurrency == null || _analyzeConcurrencyLimit != limit)
                    {
                        _analyzeConcurrency?.Dispose();
                        _analyzeConcurrency = new SemaphoreSlim(limit, limit);
                        _analyzeConcurrencyLimit = limit;
                    }

                    return _analyzeConcurrency;
                }
            }
        }

        sealed class ListCacheEntry
        {
            public DateTime FetchedAtUtc;
            public List<TracksDB.TorrentInfo> Torrents;
            public bool ServerError;
        }

        internal static async Task<IDisposable> AcquireAnalyzeSlotAsync(CancellationToken token)
        {
            var sem = AnalyzeConcurrency;
            await sem.WaitAsync(token).ConfigureAwait(false);
            return new SemaphoreReleaser(sem);
        }

        sealed class SemaphoreReleaser : IDisposable
        {
            SemaphoreSlim _sem;
            public SemaphoreReleaser(SemaphoreSlim sem) => _sem = sem;
            public void Dispose()
            {
                var s = Interlocked.Exchange(ref _sem, null);
                s?.Release();
            }
        }

        static HttpClient GetHttpClient(string tsuri)
        {
            string key = NormalizeTsuriKey(tsuri);
            return _httpClients.GetOrAdd(key, _ =>
            {
                var c = new HttpClient
                {
                    // Per-call timeouts use linked CTS; keep a high ceiling on the shared client.
                    Timeout = Timeout.InfiniteTimeSpan
                };
                AddBasicAuthHeader(c, tsuri);
                c.DefaultRequestHeaders.Accept.Clear();
                c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return c;
            });
        }

        static string NormalizeTsuriKey(string tsuri)
        {
            try
            {
                var uri = new Uri(tsuri);
                return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}".TrimEnd('/');
            }
            catch
            {
                return tsuri;
            }
        }

        static void InvalidateListCache(string tsuri)
        {
            _listCache.TryRemove(NormalizeTsuriKey(tsuri), out _);
        }

        /// <summary>
        /// Fetches TorrServer torrent list once (with short TTL cache) for load-balance, exists, and cleanup.
        /// </summary>
        static async Task<(List<TracksDB.TorrentInfo> torrents, bool serverError)> GetTorrentList(
            string tsuri, CancellationToken token, bool forceRefresh = false)
        {
            string key = NormalizeTsuriKey(tsuri);

            if (!forceRefresh &&
                _listCache.TryGetValue(key, out var cached) &&
                DateTime.UtcNow - cached.FetchedAtUtc < ListCacheTtl)
            {
                return (cached.Torrents, cached.ServerError);
            }

            try
            {
                var client = GetHttpClient(tsuri);

                var jsonContent = JsonConvert.SerializeObject(new { action = "list" });
                using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                using var response = await client.PostAsync($"{tsuri}/torrents", content, cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var entry = new ListCacheEntry
                    {
                        FetchedAtUtc = DateTime.UtcNow,
                        Torrents = null,
                        ServerError = true
                    };
                    _listCache[key] = entry;
                    return (null, true);
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                var torrents = JsonConvert.DeserializeObject<List<TracksDB.TorrentInfo>>(jsonResponse)
                               ?? new List<TracksDB.TorrentInfo>();

                _listCache[key] = new ListCacheEntry
                {
                    FetchedAtUtc = DateTime.UtcNow,
                    Torrents = torrents,
                    ServerError = false
                };

                return (torrents, false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                _listCache[key] = new ListCacheEntry
                {
                    FetchedAtUtc = DateTime.UtcNow,
                    Torrents = null,
                    ServerError = true
                };
                return (null, true);
            }
        }

        static TracksDB.TorrentInfo FindTorrentInList(List<TracksDB.TorrentInfo> torrents, string infohash)
        {
            if (torrents == null || string.IsNullOrEmpty(infohash))
                return null;

            return torrents.FirstOrDefault(t =>
                (!string.IsNullOrEmpty(t.hash) &&
                 t.hash.Equals(infohash, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(t.name) &&
                 t.name.EndsWith(infohash, StringComparison.OrdinalIgnoreCase)));
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
            var serverTasks = servers.Select(async server =>
            {
                var (torrents, serverError) = await GetTorrentList(server, token).ConfigureAwait(false);
                if (serverError || torrents == null)
                    return (server, count: 0, isValid: false);

                int count = torrents.Count(t =>
                    !string.IsNullOrEmpty(t.category) &&
                    t.category.Equals(expectedCategory, StringComparison.OrdinalIgnoreCase));

                return (server, count, isValid: true);
            });

            var results = await Task.WhenAll(serverTasks).ConfigureAwait(false);
            var validServers = results.Where(r => r.isValid).ToList();
            if (validServers.Count == 0)
                return null;

            return validServers.OrderBy(r => r.count).First().server;
        }

        static void AddBasicAuthHeader(HttpClient client, string url)
        {
            try
            {
                var uri = new Uri(url);
                if (string.IsNullOrEmpty(uri.UserInfo))
                    return;

                var credentials = uri.UserInfo.Split(':');
                if (credentials.Length != 2)
                    return;

                var byteArray = Encoding.ASCII.GetBytes($"{credentials[0]}:{credentials[1]}");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка при добавлении Basic Auth: {ex.Message}");
            }
        }

        static async Task<(bool exists, string category, bool serverError)> CheckTorrentExistsWithCategory(
            string tsuri, string infohash, CancellationToken token, int? typetask = null, bool forceRefresh = false)
        {
            var (torrents, serverError) = await GetTorrentList(tsuri, token, forceRefresh).ConfigureAwait(false);

            if (serverError)
            {
                TracksDB.Log($"Сервер вернул ошибку при запросе списка торрентов", typetask);
                return (false, null, true);
            }

            if (torrents == null)
            {
                TracksDB.Log("Получен пустой список торрентов", typetask);
                return (false, null, false);
            }

            if (string.IsNullOrEmpty(infohash))
                return (false, null, false);

            var torrent = FindTorrentInList(torrents, infohash);
            if (torrent == null)
                return (false, null, false);

            return (true, torrent.category ?? string.Empty, false);
        }

        static async Task<(bool added, bool existsInCorrectCategory, bool serverError)> AddTorrentToServer(
            string tsuri, string magnet, string infohash, string expectedCategory, CancellationToken token, int? typetask = null)
        {
            try
            {
                (bool exists, string actualCategory, bool serverError) =
                    await CheckTorrentExistsWithCategory(tsuri, infohash, token, typetask).ConfigureAwait(false);

                if (serverError)
                    return (false, false, true);

                if (exists)
                {
                    bool isCorrectCategory = actualCategory?.Equals(expectedCategory, StringComparison.OrdinalIgnoreCase) ?? false;
                    if (isCorrectCategory)
                        return (false, true, false);

                    return (false, false, false);
                }

                var client = GetHttpClient(tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "add",
                    link = magnet,
                    save_to_db = false,
                    category = expectedCategory
                });

                using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                using var response = await client.PostAsync($"{tsuri}/torrents", content, cts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    await response.Content.CopyToAsync(Stream.Null, cts.Token).ConfigureAwait(false);
                    InvalidateListCache(tsuri);
                    return (true, false, false);
                }

                TracksDB.Log($"Ошибка при добавлении торрента ({(int)response.StatusCode})", typetask);
                return (false, false, false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
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
            // File index 1: movies/TV almost always put the main media first.
            string apiUrl = $"{tsuri}/ffp/{infohash.ToUpper()}/1";

            var client = GetHttpClient(tsuri);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromMinutes(2));

            using var response = await client.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            int statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
                return (null, statusCode);

            string jsonResponse = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

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
                (bool exists, string actualCategory, bool serverError) =
                    await CheckTorrentExistsWithCategory(tsuri, infohash, CancellationToken.None, typetask, forceRefresh: true)
                        .ConfigureAwait(false);

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

                var client = GetHttpClient(tsuri);

                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "rem",
                    hash = infohash
                });

                using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                using var response = await client.PostAsync($"{tsuri}/torrents", content, cts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    InvalidateListCache(tsuri);
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
