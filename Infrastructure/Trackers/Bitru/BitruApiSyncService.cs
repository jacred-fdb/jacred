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
        const string TrackerName = "bitruapi";

        static readonly string ApiUrl;
        static readonly string HostUrl;
        static readonly string LastNewTorPath = "Data/temp/bitruapi_lastnewtor.txt";

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        static BitruApiSyncService()
        {
            var host = AppInit.conf.Bitru?.host?.TrimEnd('/') ?? "https://bitru.org";
            ApiUrl = $"{host}/api.php";
            HostUrl = host;
        }

        public async Task<string> ParseAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName, $"Parse start, limit={limit}, api={ApiUrl}");

                    var torrents = await FetchTorrentsFromApi(limit: Math.Min(100, limit), afterDateUnix: null, cancellationToken);
                    if (torrents != null && torrents.Count > 0)
                    {
                        await SaveTorrentsAndMagnets(torrents, cancellationToken);
                        log = $"saved {torrents.Count}";
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
            });
        }

        public async Task<string> ParseFromDateAsync(string lastnewtor, int limit = 100, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(lastnewtor))
                return "bad lastnewtor (use dd.MM.yyyy)";

            if (!DateTime.TryParseExact(lastnewtor.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fromDate))
                return "bad date format (use dd.MM.yyyy)";

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    long unixFrom = DateTimeOffset.UtcNow.Date == fromDate.Date
                        ? DateTimeOffset.FromUnixTimeSeconds(0).ToUnixTimeSeconds()
                        : new DateTimeOffset(fromDate.Year, fromDate.Month, fromDate.Day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

                    ParserLog.Write(TrackerName, $"ParseFromDate lastnewtor={lastnewtor} (unix={unixFrom}), limit={limit}");

                    var torrents = await FetchTorrentsFromApi(limit: Math.Min(100, limit), afterDateUnix: unixFrom, cancellationToken);
                    if (torrents != null && torrents.Count > 0)
                    {
                        await SaveTorrentsAndMagnets(torrents, cancellationToken);
                        log = $"saved {torrents.Count}";
                    }
                    else
                        log = "no items";

                    ParserLog.Write(TrackerName, $"ParseFromDate completed in {sw.Elapsed.TotalSeconds:F1}s, {log}");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                    log = $"error: {ex.Message}";
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            });
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

        async Task<List<TorrentDetails>> FetchTorrentsFromApi(int limit, long? afterDateUnix, CancellationToken cancellationToken)
        {
            var all = new List<TorrentDetails>();
            var currentParams = new Dictionary<string, object>
            {
                { "limit", limit },
                { "category", new[] { "movie", "serial" } }
            };
            if (afterDateUnix.HasValue)
                currentParams["after_date"] = afterDateUnix.Value.ToString();

            for (int page = 0; page < 50; page++)
            {
                await Task.Delay(ApiDelayMs, cancellationToken);

                var resp = await ApiRequestAsync(currentParams, cancellationToken);
                if (resp == null || resp.Error || resp.Result?.Items == null)
                {
                    if (resp != null && resp.Error && !string.IsNullOrEmpty(resp.Message))
                        ParserLog.Write(TrackerName, $"API error: {resp.Message}");
                    break;
                }

                foreach (var wrap in resp.Result.Items)
                {
                    if (wrap?.Item == null)
                        continue;
                    var t = BitruApiParser.MapToTorrentDetails(wrap.Item, HostUrl);
                    if (t != null)
                        all.Add(t);
                }

                if (resp.Result.Items.Count == 0)
                    break;

                object nextDate = resp.Result.BeforeDate;
                if (nextDate == null)
                    break;

                long beforeUnix = 0;
                if (nextDate is long l)
                    beforeUnix = l;
                else if (nextDate is string s && long.TryParse(s, out long parsed))
                    beforeUnix = parsed;

                if (beforeUnix == 0)
                    break;

                currentParams = new Dictionary<string, object>
                {
                    { "limit", limit },
                    { "category", new[] { "movie", "serial" } },
                    { "before_date", beforeUnix.ToString() }
                };
            }

            return all;
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
                    downloadUrl = idMatch.Success ? $"{HostUrl}/download.php?id={idMatch.Groups[1].Value}" : null;
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
    }
}
