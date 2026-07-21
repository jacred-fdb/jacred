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

        static TimeSpan GetTracksReadTimeout() =>
            TimeSpan.FromSeconds(Math.Max(1, AppInit.conf?.tracksreadtimeout ?? 30));

        static TimeSpan GetTracksPeerWaitTimeout() =>
            TimeSpan.FromSeconds(Math.Max(1, AppInit.conf?.trackspeerwaittimeout ?? 30));

        static TimeSpan GetFfpTimeout(int sid, TracksDB.TorrentInfo peerInfo = null)
        {
            if (peerInfo != null && peerInfo.connected_seeders == 0 && peerInfo.bytes_read > 0)
                return TimeSpan.FromSeconds(Math.Max(1, AppInit.conf?.tracksffptimeoutnosid ?? 30));

            return TimeSpan.FromSeconds(Math.Max(1, sid > 0
                ? AppInit.conf?.tracksffptimeout ?? 60
                : AppInit.conf?.tracksffptimeoutnosid ?? 30));
        }

        static int GetFfpRetryExtra() => Math.Max(0, AppInit.conf?.tracksffpretry ?? 2);

        static long GetMinBufferBytes() =>
            Math.Max(0, (long)(AppInit.conf?.tracksminbufferkb ?? 512) * 1024);

        static TimeSpan GetAnalyzeOverallTimeout(int sid)
        {
            int extraRetries = GetFfpRetryExtra();
            return GetTracksReadTimeout()
                   + GetTracksPeerWaitTimeout()
                   + GetTracksPeerWaitTimeout() // buffer wait budget
                   + GetFfpTimeout(sid) * (1 + extraRetries)
                   + TimeSpan.FromSeconds(30);
        }

        static bool HasDownloadProgress(TracksDB.TorrentInfo info) =>
            info != null && (info.bytes_read > 0 || info.connected_seeders > 0);

        static readonly ConcurrentDictionary<string, SemaphoreSlim> _hashLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        static readonly ConcurrentDictionary<string, byte> _inFlightHashes =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        internal static System.Collections.Generic.HashSet<string> GetInFlightHashes() =>
            new System.Collections.Generic.HashSet<string>(_inFlightHashes.Keys, StringComparer.OrdinalIgnoreCase);

        internal static void RegisterInFlight(string infohash)
        {
            if (!string.IsNullOrEmpty(infohash))
                _inFlightHashes.TryAdd(infohash, 0);
        }

        internal static void UnregisterInFlight(string infohash)
        {
            if (!string.IsNullOrEmpty(infohash))
                _inFlightHashes.TryRemove(infohash, out _);
        }

        /// <summary>Returns false if the same hash is already being analyzed.</summary>
        internal static async Task<IDisposable> TryAcquireHashLockAsync(string infohash, CancellationToken token)
        {
            if (string.IsNullOrEmpty(infohash))
                return NoopDisposable.Instance;

            var sem = _hashLocks.GetOrAdd(infohash, _ => new SemaphoreSlim(1, 1));
            if (!await sem.WaitAsync(0, token).ConfigureAwait(false))
                return null;

            return new HashLockReleaser(infohash, sem);
        }

        sealed class HashLockReleaser : IDisposable
        {
            readonly string _hash;
            SemaphoreSlim _sem;

            public HashLockReleaser(string hash, SemaphoreSlim sem)
            {
                _hash = hash;
                _sem = sem;
            }

            public void Dispose()
            {
                var sem = Interlocked.Exchange(ref _sem, null);
                sem?.Release();
                if (sem != null && sem.CurrentCount == 1)
                    _hashLocks.TryRemove(_hash, out _);
            }
        }

        sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new NoopDisposable();
            public void Dispose() { }
        }

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

        static async Task<(bool added, bool existsInCorrectCategory, bool serverError, bool addAttempted)> AddTorrentToServer(
            string tsuri, string magnet, string infohash, string expectedCategory, CancellationToken token, int? typetask = null)
        {
            try
            {
                (bool exists, string actualCategory, bool serverError) =
                    await CheckTorrentExistsWithCategory(tsuri, infohash, token, typetask).ConfigureAwait(false);

                if (serverError)
                    return (false, false, true, false);

                if (exists)
                {
                    bool isCorrectCategory = actualCategory?.Equals(expectedCategory, StringComparison.OrdinalIgnoreCase) ?? false;
                    if (isCorrectCategory)
                        return (false, true, false, false);

                    return (false, false, false, false);
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
                    return (true, false, false, true);
                }

                TracksDB.Log($"Ошибка при добавлении торрента ({(int)response.StatusCode})", typetask);
                // Request reached the server; rem may still be needed if TS accepted it.
                return (false, false, false, true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                TracksDB.Log("Таймаут при добавлении торрента на сервер", typetask);
                // Add may have succeeded on TS before the client timed out — rem this hash.
                return (false, false, true, true);
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка при добавлении торрента на сервере: {ex.Message}", typetask);
                return (false, false, true, true);
            }
        }

        internal static async Task<(List<TracksDB.TorrentInfo> torrents, bool serverError)> GetTorrentListForCleanup(
            string tsuri, CancellationToken token) =>
            await GetTorrentList(tsuri, token, forceRefresh: true).ConfigureAwait(false);

        internal static async Task<bool> RemTorrentOnServer(string tsuri, string infohash, int? typetask = null)
        {
            try
            {
                var client = GetHttpClient(tsuri);

                async Task<bool> PostRemAsync()
                {
                    var jsonContent = JsonConvert.SerializeObject(new { action = "rem", hash = infohash });
                    using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var response = await client.PostAsync($"{tsuri}/torrents", content, cts.Token).ConfigureAwait(false);
                    return response.IsSuccessStatusCode;
                }

                if (await PostRemAsync().ConfigureAwait(false))
                {
                    InvalidateListCache(tsuri);
                    return true;
                }

                TracksDB.Log($"rem {infohash}: retry after failure", typetask);
                await Task.Delay(2000).ConfigureAwait(false);

                if (await PostRemAsync().ConfigureAwait(false))
                {
                    InvalidateListCache(tsuri);
                    return true;
                }

                InvalidateListCache(tsuri);
                return false;
            }
            catch (Exception ex)
            {
                TracksDB.Log($"rem {infohash} ошибка: {ex.Message}", typetask);
                return false;
            }
        }

        internal static async Task<(FfprobeModel result, int statusCode)> AnalyzeWithExternalApi(
            string tsuri, string infohash, CancellationToken token, int? typetask = null,
            TimeSpan? ffpTimeout = null, int fileId = 1)
        {
            var timeout = ffpTimeout ?? GetFfpTimeout(1);
            string apiUrl = $"{tsuri}/ffp/{infohash.ToUpper()}/{fileId}";

            TracksDB.Log($"Запрос /ffp/{fileId} для {infohash} (таймаут {timeout.TotalSeconds:F0}s)...", typetask);

            var client = GetHttpClient(tsuri);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);

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

        internal static async Task<(FfprobeModel result, int statusCode, string errorMessage)> ProbeFfpWithRetries(
            string tsuri, string infohash, IReadOnlyList<int> fileIds, TimeSpan ffpTimeout,
            CancellationToken token, int? typetask = null)
        {
            if (fileIds == null || fileIds.Count == 0)
                fileIds = new[] { 1 };

            int lastCode = 0;
            foreach (var fileId in fileIds)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var (result, code) = await AnalyzeWithExternalApi(
                        tsuri, infohash, token, typetask, ffpTimeout, fileId).ConfigureAwait(false);

                    lastCode = code;

                    if (result?.streams != null && result.streams.Count > 0)
                        return (result, code, null);

                    if (code != 400)
                        return (result, code, "Нет данных о треках");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return (null, 504, "ffp timeout");
                }
            }

            return (null, lastCode > 0 ? lastCode : 400, "no probeable media file");
        }

        /// <summary>
        /// Polls TorrServer <c>action=get</c> until metadata is ready (<c>file_stats</c> present)
        /// or <paramref name="readyTimeout"/> elapses. Reduces false HTTP 400 from /ffp before GotInfo.
        /// </summary>
        internal static async Task<bool> WaitTorrentReady(
            string tsuri, string infohash, CancellationToken token, int? typetask = null,
            TimeSpan? readyTimeout = null)
        {
            var timeout = readyTimeout ?? GetTracksReadTimeout();
            var deadline = DateTime.UtcNow + timeout;
            var pollInterval = TimeSpan.FromSeconds(2);

            TracksDB.Log($"Ожидание готовности торрента {infohash} (до {timeout.TotalSeconds:F0}s)...", typetask);

            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();

                var info = await GetTorrentFromServer(tsuri, infohash, token, typetask).ConfigureAwait(false);
                if (info?.file_stats != null && info.file_stats.Count > 0)
                {
                    TracksDB.Log($"Торрент {infohash} готов: file_stats={info.file_stats.Count}, stat={info.stat}", typetask);
                    return true;
                }

                // TorrServer: TorrentWorking = 3
                if (info != null && info.stat == 3)
                {
                    TracksDB.Log($"Торрент {infohash} в состоянии Working (stat=3), file_stats ещё пуст — продолжаем ожидание", typetask);
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                await Task.Delay(remaining < pollInterval ? remaining : pollInterval, token).ConfigureAwait(false);
            }

            TracksDB.Log($"Торрент {infohash} не получил file_stats за {timeout.TotalSeconds:F0}s — проверяем сиды перед /ffp", typetask);
            return false;
        }

        /// <summary>
        /// Polls TorrServer until download progress is visible (seeders or bytes_read)
        /// or <paramref name="progressTimeout"/> elapses.
        /// </summary>
        internal static async Task<bool> WaitDownloadProgress(
            string tsuri, string infohash, CancellationToken token, int? typetask = null,
            TimeSpan? progressTimeout = null)
        {
            var timeout = progressTimeout ?? GetTracksPeerWaitTimeout();
            var deadline = DateTime.UtcNow + timeout;
            var pollInterval = TimeSpan.FromSeconds(2);

            TracksDB.Log($"Проверка прогресса загрузки {infohash} (до {timeout.TotalSeconds:F0}s)...", typetask);

            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();

                var info = await GetTorrentFromServer(tsuri, infohash, token, typetask).ConfigureAwait(false);
                if (HasDownloadProgress(info))
                {
                    TracksDB.Log(
                        $"Торрент {infohash}: прогресс загрузки (seeders={info.connected_seeders}, bytes_read={info.bytes_read})",
                        typetask);
                    return true;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                await Task.Delay(remaining < pollInterval ? remaining : pollInterval, token).ConfigureAwait(false);
            }

            TracksDB.Log($"Торрент {infohash}: нет сидов/данных — пропуск /ffp", typetask);
            return false;
        }

        /// <summary>
        /// Waits until enough data is buffered for ffprobe, or progress stalls.
        /// </summary>
        internal static async Task<bool> WaitMediaBuffer(
            string tsuri, string infohash, CancellationToken token, int? typetask = null,
            TimeSpan? bufferTimeout = null)
        {
            long minBytes = GetMinBufferBytes();
            if (minBytes <= 0)
                return true;

            var timeout = bufferTimeout ?? GetTracksPeerWaitTimeout();
            var deadline = DateTime.UtcNow + timeout;
            var pollInterval = TimeSpan.FromSeconds(2);
            long lastLoaded = -1;
            int stallPolls = 0;

            TracksDB.Log($"Ожидание буфера {infohash} (мин. {minBytes} bytes, до {timeout.TotalSeconds:F0}s)...", typetask);

            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();

                var info = await GetTorrentFromServer(tsuri, infohash, token, typetask).ConfigureAwait(false);
                long loaded = Math.Max(info?.loaded_size ?? 0, info?.bytes_read ?? 0);

                if (loaded >= minBytes)
                {
                    TracksDB.Log($"Торрент {infohash}: буфер готов (loaded={loaded})", typetask);
                    return true;
                }

                if (info != null && info.connected_seeders == 0 && info.download_speed == 0 && loaded > 0)
                {
                    stallPolls++;
                    if (stallPolls >= 3)
                    {
                        TracksDB.Log($"Торрент {infohash}: загрузка остановилась (loaded={loaded})", typetask);
                        return loaded > 0;
                    }
                }
                else
                {
                    stallPolls = 0;
                }

                if (loaded == lastLoaded && info?.connected_seeders == 0 && loaded == 0)
                {
                    // no progress at all — fail fast inside peer window
                    break;
                }

                lastLoaded = loaded;

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                await Task.Delay(remaining < pollInterval ? remaining : pollInterval, token).ConfigureAwait(false);
            }

            TracksDB.Log($"Торрент {infohash}: буфер не достигнут ({minBytes} bytes)", typetask);
            return false;
        }

        internal static async Task<TracksDB.TorrentInfo> GetTorrentFromServer(
            string tsuri, string infohash, CancellationToken token, int? typetask = null)
        {
            try
            {
                var client = GetHttpClient(tsuri);
                var jsonContent = JsonConvert.SerializeObject(new
                {
                    action = "get",
                    hash = infohash
                });

                using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                using var response = await client.PostAsync($"{tsuri}/torrents", content, cts.Token).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;

                if (!response.IsSuccessStatusCode)
                {
                    TracksDB.Log($"get torrent вернул {(int)response.StatusCode}", typetask);
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(jsonResponse))
                    return null;

                return JsonConvert.DeserializeObject<TracksDB.TorrentInfo>(jsonResponse);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                TracksDB.Log($"Ошибка get torrent: {ex.Message}", typetask);
                return null;
            }
            catch (HttpRequestException ex)
            {
                TracksDB.Log($"Ошибка get torrent: {ex.Message}", typetask);
                return null;
            }
            catch (JsonException ex)
            {
                TracksDB.Log($"Ошибка get torrent: {ex.Message}", typetask);
                return null;
            }
        }

        /// <summary>
        /// Removes the torrent from TorrServer when we know it is in our category.
        /// Skips the list round-trip — ownership is tracked in-memory from add/exists.
        /// </summary>
        static async Task CleanupTorrent(
            string tsuri, string infohash, string expectedCategory, int? typetask = null, bool ownedInCategory = false)
        {
            try
            {
                if (!ownedInCategory)
                {
                    TracksDB.Log($"Торрент {infohash}: rem пропущен (не добавляли / чужая категория).", typetask);
                    return;
                }

                if (await RemTorrentOnServer(tsuri, infohash, typetask).ConfigureAwait(false))
                    TracksDB.Log($"Торрент {infohash} успешно удален с сервера", typetask);
                else
                    TracksDB.Log($"Ошибка при удалении торрента {infohash}", typetask);
            }
            catch (Exception ex)
            {
                TracksDB.Log($"Ошибка при очистке торрента {infohash} на сервере: {ex.Message}", typetask);
            }
        }
    }
}
