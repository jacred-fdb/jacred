using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.Bitru
{
    public class BitruApiSyncService
    {
        const string ApiGetTorrents = "torrents";
        const int ApiDelayMs = 250;
        const string TrackerName = "bitru";

        static readonly string ApiUrl;
        static readonly string HostUrl;
        static readonly string LastNewTorPath = "Data/temp/bitru_lastnewtor.txt";
        static readonly string BackfillCursorPath = "Data/temp/bitru_backfill_cursor.txt";
        const string LegacyLastNewTorPath = "Data/temp/bitruapi_lastnewtor.txt";

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        static BitruApiSyncService()
        {
            var host = AppInit.conf.Bitru?.host?.TrimEnd('/') ?? "https://bitru.org";
            ApiUrl = $"{host}/api.php";
            HostUrl = host;
            MigrateLegacyLastNewTorFile();
        }

        static void MigrateLegacyLastNewTorFile()
        {
            try
            {
                if (IO.File.Exists(LastNewTorPath) || !IO.File.Exists(LegacyLastNewTorPath))
                    return;
                IO.File.Move(LegacyLastNewTorPath, LastNewTorPath);
            }
            catch (IO.IOException ex)
            {
                ParserLog.Write(TrackerName, $"Legacy lastnewtor migration failed: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                ParserLog.Write(TrackerName, $"Legacy lastnewtor migration failed: {ex.Message}");
            }
        }

        /// <summary>Newest page only — used by regular cron.</summary>
        public async Task<string> ParseAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    int lim = BitruApiPagination.ClampLimit(limit);
                    ParserLog.Write(TrackerName, $"Parse start, limit={lim}, maxPages=1, api={ApiUrl}");

                    var fetch = await FetchTorrentsFromApi(
                        limit: lim,
                        olderThanUnix: null,
                        maxPages: 1,
                        persistBackfillCursor: false,
                        cancellationToken);

                    if (fetch.Torrents.Count > 0)
                    {
                        await SaveTorrentsAndMagnets(fetch.Torrents, cancellationToken);
                        log = $"saved {fetch.Torrents.Count}";
                    }
                    else
                        log = "no items";

                    ParserLog.Write(TrackerName, $"Parse completed in {sw.Elapsed.TotalSeconds:F1}s, {log}");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                    log = $"error: {ex.Message}";
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            }, cancellationToken);
        }

        /// <summary>
        /// Walk older archive pages. Live API: after_date means older-than (docs label is inverted).
        /// Progress is stored in Data/temp/bitru_backfill_cursor.txt as unix seconds.
        /// </summary>
        public async Task<string> BackfillAsync(int pages = 20, int limit = 100, CancellationToken cancellationToken = default)
        {
            int maxPages = BitruApiPagination.ClampPages(pages);
            int lim = BitruApiPagination.ClampLimit(limit);
            long? startCursor = ReadBackfillCursor();

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName,
                        $"Backfill start, pages={maxPages}, limit={lim}, cursor={(startCursor.HasValue ? startCursor.Value.ToString(CultureInfo.InvariantCulture) : "none")}, api={ApiUrl}");

                    var fetch = await FetchTorrentsFromApi(
                        limit: lim,
                        olderThanUnix: startCursor,
                        maxPages: maxPages,
                        persistBackfillCursor: true,
                        cancellationToken);

                    if (fetch.Torrents.Count > 0)
                    {
                        await SaveTorrentsAndMagnets(fetch.Torrents, cancellationToken);
                        log = $"saved {fetch.Torrents.Count}, pages={fetch.PagesFetched}, cursor={fetch.LastCursor?.ToString(CultureInfo.InvariantCulture) ?? "none"}";
                    }
                    else
                        log = $"no items, pages={fetch.PagesFetched}, cursor={fetch.LastCursor?.ToString(CultureInfo.InvariantCulture) ?? startCursor?.ToString(CultureInfo.InvariantCulture) ?? "none"}";

                    ParserLog.Write(TrackerName, $"Backfill completed in {sw.Elapsed.TotalSeconds:F1}s, {log}");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                    log = $"error: {ex.Message}";
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            }, cancellationToken);
        }

        /// <summary>
        /// Fetch torrents older than the given calendar day (live after_date = older-than).
        /// Writes backfill cursor so Backfill can continue.
        /// </summary>
        public async Task<string> ParseFromDateAsync(string lastnewtor, int limit = 100, int pages = 20, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(lastnewtor))
                return "bad lastnewtor (use dd.MM.yyyy)";

            if (!DateTime.TryParseExact(lastnewtor.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fromDate))
                return "bad date format (use dd.MM.yyyy)";

            int maxPages = BitruApiPagination.ClampPages(pages);
            int lim = BitruApiPagination.ClampLimit(limit);
            long unixFrom = BitruApiParser.UnixFromDate(fromDate);

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName,
                        $"ParseFromDate lastnewtor={lastnewtor} (olderThan unix={unixFrom}), pages={maxPages}, limit={lim}");

                    var fetch = await FetchTorrentsFromApi(
                        limit: lim,
                        olderThanUnix: unixFrom,
                        maxPages: maxPages,
                        persistBackfillCursor: true,
                        cancellationToken);

                    if (fetch.Torrents.Count > 0)
                    {
                        await SaveTorrentsAndMagnets(fetch.Torrents, cancellationToken);
                        log = $"saved {fetch.Torrents.Count}, pages={fetch.PagesFetched}, cursor={fetch.LastCursor?.ToString(CultureInfo.InvariantCulture) ?? "none"}";
                    }
                    else
                        log = $"no items, pages={fetch.PagesFetched}";

                    ParserLog.Write(TrackerName, $"ParseFromDate completed in {sw.Elapsed.TotalSeconds:F1}s, {log}");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                    log = $"error: {ex.Message}";
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            }, cancellationToken);
        }

        async Task<BitruApiResponse> ApiRequestAsync(object jsonParams, CancellationToken cancellationToken)
        {
            string json = JsonConvert.SerializeObject(jsonParams);
            string postData = $"get={ApiGetTorrents}&json={Uri.EscapeDataString(json)}";
            cancellationToken.ThrowIfCancellationRequested();
            string response = await HttpClient.Post(ApiUrl, postData, timeoutSeconds: 15, useproxy: AppInit.conf.Bitru.useproxy);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            return JsonConvert.DeserializeObject<BitruApiResponse>(response);
        }

        async Task<FetchResult> FetchTorrentsFromApi(
            int limit,
            long? olderThanUnix,
            int maxPages,
            bool persistBackfillCursor,
            CancellationToken cancellationToken)
        {
            var all = new List<TorrentDetails>();
            int pagesFetched = 0;
            long? lastCursor = olderThanUnix;
            long? requestCursor = olderThanUnix;
            HashSet<long> previousIds = null;

            maxPages = BitruApiPagination.ClampPages(maxPages);
            var currentParams = BitruApiPagination.BuildRequestParams(limit, requestCursor);

            for (int page = 0; page < maxPages; page++)
            {
                await Task.Delay(ApiDelayMs, cancellationToken);

                var resp = await ApiRequestAsync(currentParams, cancellationToken);
                if (resp == null || resp.HasError || resp.Result?.Items == null)
                {
                    if (resp != null && resp.HasError && !string.IsNullOrEmpty(resp.ErrorMessage))
                        ParserLog.Write(TrackerName, $"API error: {resp.ErrorMessage}");
                    break;
                }

                if (resp.Result.Items.Count == 0)
                    break;

                var pageTorrents = BitruApiParser.ParseTorrentsFromResponse(resp, HostUrl);
                var pageIds = BitruApiPagination.CollectTorrentIds(pageTorrents.Select(t => t.url));

                if (BitruApiPagination.IsDuplicatePage(previousIds, pageIds))
                {
                    ParserLog.Write(TrackerName, $"Stop: page {page + 1} fully overlaps previous page");
                    break;
                }

                all.AddRange(pageTorrents);
                pagesFetched++;
                previousIds = pageIds;

                if (!BitruApiPagination.TryGetNextOlderPageCursor(resp.Result, requestCursor, out long nextCursor))
                    break;

                lastCursor = nextCursor;
                if (persistBackfillCursor)
                    WriteBackfillCursor(nextCursor);

                if (page + 1 >= maxPages)
                    break;

                requestCursor = nextCursor;
                currentParams = BitruApiPagination.BuildRequestParams(limit, nextCursor);
            }

            return new FetchResult(all, lastCursor, pagesFetched);
        }

        long? ReadBackfillCursor()
        {
            try
            {
                if (!IO.File.Exists(BackfillCursorPath))
                    return null;
                string text = IO.File.ReadAllText(BackfillCursorPath).Trim();
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unix) && unix > 0)
                    return unix;
            }
            catch (Exception ex)
            {
                ParserLog.Write(TrackerName, $"Read backfill cursor failed: {ex.Message}");
            }
            return null;
        }

        void WriteBackfillCursor(long unix)
        {
            try
            {
                string dir = IO.Path.GetDirectoryName(BackfillCursorPath);
                if (!string.IsNullOrEmpty(dir))
                    IO.Directory.CreateDirectory(dir);
                IO.File.WriteAllText(BackfillCursorPath, unix.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                ParserLog.Write(TrackerName, $"Write backfill cursor failed: {ex.Message}");
            }
        }

        async Task SaveTorrentsAndMagnets(List<TorrentDetails> torrents, CancellationToken cancellationToken)
        {
            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                string downloadUrl = t._sn;
                if (string.IsNullOrWhiteSpace(downloadUrl) || !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var idMatch = System.Text.RegularExpressions.Regex.Match(t.url ?? "", @"\?id=(\d+)");
                    downloadUrl = idMatch.Success ? $"{HostUrl}/api.php?download={idMatch.Groups[1].Value}" : null;
                }
                if (string.IsNullOrWhiteSpace(downloadUrl))
                    return false;

                await Task.Delay(ApiDelayMs, cancellationToken);

                byte[] data = await HttpClient.Download(downloadUrl, referer: HostUrl + "/", timeoutSeconds: 15, useproxy: AppInit.conf.Bitru.useproxy);
                string magnet = data != null ? BencodeTo.Magnet(data) : null;
                if (!string.IsNullOrWhiteSpace(magnet))
                {
                    t.magnet = magnet;
                    t._sn = null;
                    return true;
                }

                return false;
            });

            try
            {
                var lastTor = torrents.OrderByDescending(x => x.createTime).FirstOrDefault();
                if (lastTor != null)
                    IO.File.WriteAllText(LastNewTorPath, lastTor.createTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            }
            catch { }
        }

        readonly struct FetchResult
        {
            public FetchResult(List<TorrentDetails> torrents, long? lastCursor, int pagesFetched)
            {
                Torrents = torrents ?? new List<TorrentDetails>();
                LastCursor = lastCursor;
                PagesFetched = pagesFetched;
            }

            public List<TorrentDetails> Torrents { get; }
            public long? LastCursor { get; }
            public int PagesFetched { get; }
        }
    }
}
