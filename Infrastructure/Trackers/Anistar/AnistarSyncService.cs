using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Anistar
{
    public class AnistarSyncService
    {
        const string TrackerName = "anistar";

        static readonly Encoding PageEncoding = Encoding.GetEncoding(1251);

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        public async Task<string> ParseAsync(int limitPage = 0)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    string host = (AppInit.conf.Anistar.host ?? "").TrimEnd('/');
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        ParserLog.Write(TrackerName, "Config missing", new Dictionary<string, object> { { "reason", "empty host" } });
                        return "config missing";
                    }

                    ParserLog.Write(TrackerName, "Starting parse", new Dictionary<string, object>
                    {
                        { "limitPage", limitPage },
                        { "host", host }
                    });

                    int totalFetched = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                    foreach (var kv in AnistarCategories.Map)
                    {
                        string catPath = kv.Key;
                        string[] types = kv.Value.Types;
                        int lastPage = limitPage;
                        if (lastPage <= 0)
                        {
                            string firstHtml = await HttpClient.Get($"{host}/{catPath}/", encoding: PageEncoding, cookie: AppInit.conf.Anistar.cookie, useproxy: AppInit.conf.Anistar.useproxy);
                            lastPage = AnistarParser.DetectLastPage(firstHtml);
                        }

                        for (int page = 1; page <= lastPage; page++)
                        {
                            string listUrl = page <= 1
                                ? $"{host}/{catPath}/"
                                : $"{host}/{catPath}/page/{page}/";

                            if (page > 1 || catPath != "anime")
                                ParserLog.Write(TrackerName, "Parsing list page", new Dictionary<string, object>
                                {
                                    { "category", catPath },
                                    { "page", page },
                                    { "url", listUrl }
                                });

                            string listHtml = await HttpClient.Get(listUrl, encoding: PageEncoding, cookie: AppInit.conf.Anistar.cookie, referer: host + "/", useproxy: AppInit.conf.Anistar.useproxy);
                            if (string.IsNullOrEmpty(listHtml))
                                continue;

                            var postUrls = AnistarParser.ExtractPostUrls(listHtml, host);
                            totalFetched += postUrls.Count;

                            foreach (string postUrl in postUrls)
                            {
                                var (added, updated, skipped, failed) = await ParseDetailAndSave(postUrl, listUrl, host, types);
                                totalAdded += added;
                                totalUpdated += updated;
                                totalSkipped += skipped;
                                totalFailed += failed;

                                if (AppInit.conf.Anistar.parseDelay > 0)
                                    await Task.Delay(AppInit.conf.Anistar.parseDelay);
                            }
                        }
                    }

                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                        new Dictionary<string, object>
                        {
                            { "fetched", totalFetched },
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

                return "ok";
            });
        }

        async Task<(int added, int updated, int skipped, int failed)> ParseDetailAndSave(string postUrl, string referer, string host, string[] types)
        {
            string postHtml = await HttpClient.Get(postUrl, encoding: PageEncoding, cookie: AppInit.conf.Anistar.cookie, referer: referer, useproxy: AppInit.conf.Anistar.useproxy);
            if (string.IsNullOrEmpty(postHtml))
                return (0, 0, 0, 0);

            var torrents = AnistarParser.ParseDetailTorrents(postHtml, postUrl, types);
            if (torrents.Count == 0)
                return (0, 0, 0, 0);

            int addedCount = 0, updatedCount = 0, skippedCount = 0, failedCount = 0;

            await FileDB.AddOrUpdate(torrents, async (torrent, db) =>
            {
                var t = (AnistarDetails)torrent;
                bool exists = db.TryGetValue(t.url, out TorrentDetails cached);
                bool needMagnet = !exists
                    || !string.Equals(cached.title?.Trim(), t.title?.Trim(), StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(cached.magnet);

                if (!needMagnet)
                {
                    skippedCount++;
                    ParserLog.WriteSkipped(TrackerName, cached, "no changes");
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(t.downloadId))
                {
                    string downUrl = $"{host}/engine/gettorrent.php?id={t.downloadId}";
                    byte[] torrentFile = await HttpClient.Download(downUrl, cookie: AppInit.conf.Anistar.cookie, referer: host, useproxy: AppInit.conf.Anistar.useproxy);
                    if (torrentFile != null && torrentFile.Length > 0)
                    {
                        string magnet = BencodeTo.Magnet(torrentFile);
                        if (!string.IsNullOrWhiteSpace(magnet))
                        {
                            t.magnet = magnet;
                            string sizeName = BencodeTo.SizeName(torrentFile);
                            if (!string.IsNullOrWhiteSpace(sizeName))
                                t.sizeName = sizeName;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(t.magnet))
                {
                    failedCount++;
                    ParserLog.WriteFailed(TrackerName, t, "could not get magnet");
                    return false;
                }

                if (exists)
                {
                    updatedCount++;
                    ParserLog.WriteUpdated(TrackerName, t, "magnet");
                }
                else
                {
                    addedCount++;
                    ParserLog.WriteAdded(TrackerName, t);
                }

                return true;
            });

            return (addedCount, updatedCount, skippedCount, failedCount);
        }
    }
}
