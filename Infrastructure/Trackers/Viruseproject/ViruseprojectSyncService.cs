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

namespace JacRed.Infrastructure.Trackers.Viruseproject
{
    public class ViruseprojectSyncService
    {
        const string TrackerName = ViruseprojectParser.TrackerName;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        /// <summary>
        /// Parse category listings. limitPage &gt; 0 limits pages per category;
        /// limitPage ≤ 0 detects last page from pagination-end.
        /// </summary>
        public async Task<string> ParseAsync(int limitPage = 0)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string host = AppInit.conf.Viruseproject?.rqHost()?.TrimEnd('/');
                if (string.IsNullOrWhiteSpace(host))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Viruseproject.host");
                    return "config missing";
                }

                try
                {
                    var sw = Stopwatch.StartNew();
                    int totalFetched = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                    ParserLog.Write(TrackerName, "Starting parse", new Dictionary<string, object>
                    {
                        { "limitPage", limitPage },
                        { "host", host }
                    });

                    foreach (var kv in ViruseprojectCategories.Map)
                    {
                        string cat = kv.Key;
                        string[] types = kv.Value.Types;
                        int step = ViruseprojectParser.GetPageStep(cat);

                        string firstUrl = $"{host}/releases/{cat}?start=0";
                        string firstBody = await HttpClient.Get(
                            firstUrl,
                            encoding: Encoding.UTF8,
                            useproxy: AppInit.conf.Viruseproject.useproxy);

                        if (string.IsNullOrEmpty(firstBody))
                            continue;

                        int lastPage = ViruseprojectParser.DetectLastPage(firstBody, step);
                        int totalPages = limitPage;
                        if (totalPages <= 0 || totalPages > lastPage)
                            totalPages = lastPage;

                        for (int page = 1; page <= totalPages; page++)
                        {
                            string body;
                            string pageUrl;
                            if (page == 1)
                            {
                                body = firstBody;
                                pageUrl = firstUrl;
                            }
                            else
                            {
                                if (AppInit.conf.Viruseproject.parseDelay > 0)
                                    await Task.Delay(AppInit.conf.Viruseproject.parseDelay);

                                pageUrl = $"{host}/releases/{cat}?start={(page - 1) * step}";
                                body = await HttpClient.Get(
                                    pageUrl,
                                    encoding: Encoding.UTF8,
                                    useproxy: AppInit.conf.Viruseproject.useproxy);

                                if (string.IsNullOrEmpty(body))
                                    continue;
                            }

                            var (fetched, added, updated, skipped, failed) =
                                await ParseListingAsync(body, pageUrl, host, cat, types);

                            totalFetched += fetched;
                            totalAdded += added;
                            totalUpdated += updated;
                            totalSkipped += skipped;
                            totalFailed += failed;

                            ParserLog.Write(TrackerName, "Category page done", new Dictionary<string, object>
                            {
                                { "cat", cat },
                                { "page", page },
                                { "totalPages", totalPages },
                                { "fetched", fetched },
                                { "added", added },
                                { "skipped", skipped },
                                { "failed", failed }
                            });
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

        async Task<(int fetched, int added, int updated, int skipped, int failed)> ParseListingAsync(
            string body, string pageUrl, string host, string cat, string[] types)
        {
            var postUrls = ViruseprojectParser.ExtractPostUrls(body, host);
            if (postUrls.Count == 0)
                return (0, 0, 0, 0, 0);

            var torrents = new List<ViruseprojectDetails>();
            foreach (string postUrl in postUrls)
            {
                string detailHtml = await HttpClient.Get(
                    postUrl,
                    encoding: Encoding.UTF8,
                    referer: pageUrl,
                    useproxy: AppInit.conf.Viruseproject.useproxy);

                if (string.IsNullOrEmpty(detailHtml))
                    continue;

                torrents.AddRange(ViruseprojectParser.ParseDetailHtml(detailHtml, postUrl, host, types));
            }

            return await SaveTorrentsAsync(torrents, host);
        }

        async Task<(int fetched, int added, int updated, int skipped, int failed)> SaveTorrentsAsync(
            List<ViruseprojectDetails> torrents, string host)
        {
            int fetched = torrents.Count;
            int added = 0, updated = 0, skipped = 0, failed = 0;

            if (torrents.Count == 0)
                return (0, 0, 0, 0, 0);

            await FileDB.AddOrUpdate(torrents, async (torrent, db) =>
            {
                var t = (ViruseprojectDetails)torrent;
                bool exists = db.TryGetValue(t.url, out TorrentDetails cached);
                bool needMagnet = !exists
                    || !string.Equals(cached.title?.Trim(), t.title?.Trim(), StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(cached.magnet);

                if (!needMagnet)
                {
                    skipped++;
                    ParserLog.WriteSkipped(TrackerName, cached, "no changes");
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(t.downloadUri))
                {
                    byte[] torrentFile = await HttpClient.Download(
                        t.downloadUri,
                        referer: host,
                        useproxy: AppInit.conf.Viruseproject.useproxy);

                    if (torrentFile != null && torrentFile.Length > 0)
                    {
                        string magnet = BencodeTo.Magnet(torrentFile);
                        if (!string.IsNullOrWhiteSpace(magnet))
                        {
                            t.magnet = magnet;
                            if (string.IsNullOrWhiteSpace(t.sizeName))
                            {
                                string sizeName = BencodeTo.SizeName(torrentFile);
                                if (!string.IsNullOrWhiteSpace(sizeName))
                                    t.sizeName = sizeName;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(t.magnet) && !exists)
                {
                    failed++;
                    ParserLog.WriteFailed(TrackerName, t, "could not get magnet");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(t.magnet) && exists && !string.IsNullOrWhiteSpace(cached.magnet))
                    t.magnet = cached.magnet;

                if (string.IsNullOrWhiteSpace(t.magnet))
                {
                    failed++;
                    ParserLog.WriteFailed(TrackerName, t, "could not get magnet");
                    return false;
                }

                if (exists)
                {
                    updated++;
                    ParserLog.WriteUpdated(TrackerName, t, "magnet/title updated");
                }
                else
                {
                    added++;
                    ParserLog.WriteAdded(TrackerName, t);
                }

                return true;
            });

            return (fetched, added, updated, skipped, failed);
        }
    }
}
