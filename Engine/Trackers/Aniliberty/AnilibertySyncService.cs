using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Engine.CORE;
using JacRed.Engine.Parsing;
using JacRed.Models.Details;
using JacRed.Models.tParse;

namespace JacRed.Engine.Trackers.Aniliberty
{
    public class AnilibertySyncService
    {
        const string TrackerName = "aniliberty";

        static volatile bool workParse;
        static readonly object workParseLock = new object();

        static bool TryStartParse()
        {
            lock (workParseLock)
            {
                if (workParse)
                    return false;
                workParse = true;
                return true;
            }
        }

        static void EndParse()
        {
            lock (workParseLock)
            {
                workParse = false;
            }
        }

        public async Task<string> ParseAsync(int parseFrom = 0, int parseTo = 0, CancellationToken cancellationToken = default)
        {
            if (!TryStartParse())
                return "work";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = AppInit.conf.Aniliberty.host;

                int startPage = parseFrom > 0 ? parseFrom : 1;
                int endPage = parseTo > 0 ? parseTo : (parseFrom > 0 ? parseFrom : 1);

                if (startPage > endPage)
                {
                    int temp = startPage;
                    startPage = endPage;
                    endPage = temp;
                }

                ParserLog.Write(TrackerName, "Starting parse", new Dictionary<string, object>
                {
                    { "parseFrom", parseFrom },
                    { "parseTo", parseTo },
                    { "startPage", startPage },
                    { "endPage", endPage },
                    { "baseUrl", baseUrl }
                });

                int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;
                int lastPage = int.MaxValue;

                for (int page = startPage; page <= endPage && page <= lastPage; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (page > startPage)
                        await Task.Delay(AppInit.conf.Aniliberty.parseDelay, cancellationToken);

                    ParserLog.Write(TrackerName, "Parsing page", new Dictionary<string, object>
                    {
                        { "page", page },
                        { "url", $"{baseUrl}/api/v1/anime/torrents?page={page}&limit=50" }
                    });

                    var result = await ParsePageAsync(page, cancellationToken);
                    totalParsed += result.parsed;
                    totalAdded += result.added;
                    totalUpdated += result.updated;
                    totalSkipped += result.skipped;
                    totalFailed += result.failed;

                    if (result.lastPage > 0)
                        lastPage = result.lastPage;

                    if (page >= lastPage)
                        break;
                }

                ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                    new Dictionary<string, object>
                    {
                        { "parsed", totalParsed },
                        { "added", totalAdded },
                        { "updated", totalUpdated },
                        { "skipped", totalSkipped },
                        { "failed", totalFailed }
                    });
            }
            catch (OperationCanceledException oce)
            {
                ParserLog.Write(TrackerName, "Canceled", new Dictionary<string, object>
                {
                    { "message", oce.Message },
                    { "stackTrace", oce.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
                return "canceled";
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException)
                    throw;

                ParserLog.Write(TrackerName, "Error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
            }
            finally
            {
                EndParse();
            }

            return "ok";
        }

        async Task<(int parsed, int added, int updated, int skipped, int failed, int lastPage)> ParsePageAsync(int page, CancellationToken cancellationToken)
        {
            string url = $"{AppInit.conf.Aniliberty.host}/api/v1/anime/torrents?page={page}&limit=50";
            var response = await HttpClient.Get<AnilibertyApiResponse>(url, encoding: Encoding.UTF8, useproxy: AppInit.conf.Aniliberty.useproxy);

            if (response == null || response.Data == null || response.Data.Count == 0)
            {
                ParserLog.Write(TrackerName, "Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", url },
                    { "reason", response == null ? "null response" : "no data" }
                });
                return (0, 0, 0, 0, 0, 0);
            }

            int lastPage = response.Meta?.LastPage ?? 0;
            var torrents = AnilibertyParser.MapPageTorrents(response, AppInit.conf.Aniliberty.host);

            foreach (var apiTorrent in response.Data)
            {
                if (string.IsNullOrWhiteSpace(apiTorrent.Magnet) || apiTorrent.Release == null)
                    ParserLog.WriteFailed(TrackerName, null, $"Missing magnet or release data for torrent {apiTorrent.Id}");
                else if (string.IsNullOrWhiteSpace(apiTorrent.Release.Name?.Main?.Trim()) &&
                         string.IsNullOrWhiteSpace(apiTorrent.Release.Name?.English?.Trim()))
                    ParserLog.WriteFailed(TrackerName, null, $"Missing name for torrent {apiTorrent.Id}");
            }

            int parsedCount = torrents.Count;
            int addedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, (t, db) =>
                {
                    bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);

                    if (exists && string.Equals(_tcache.magnet?.Trim(), t.magnet?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        skippedCount++;
                        ParserLog.WriteSkipped(TrackerName, _tcache, "no changes");
                        return Task.FromResult(false);
                    }

                    if (exists)
                    {
                        updatedCount++;
                        ParserLog.WriteUpdated(TrackerName, t, "magnet changed or updated");
                    }
                    else
                    {
                        addedCount++;
                        ParserLog.WriteAdded(TrackerName, t);
                    }

                    return Task.FromResult(true);
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write(TrackerName, $"Page {page} completed",
                    new Dictionary<string, object>
                    {
                        { "parsed", parsedCount },
                        { "added", addedCount },
                        { "updated", updatedCount },
                        { "skipped", skippedCount },
                        { "failed", failedCount }
                    });
            }

            return (parsedCount, addedCount, updatedCount, skippedCount, failedCount, lastPage);
        }
    }
}
