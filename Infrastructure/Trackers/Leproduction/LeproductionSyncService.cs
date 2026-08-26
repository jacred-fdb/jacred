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

namespace JacRed.Infrastructure.Trackers.Leproduction
{
    public class LeproductionSyncService
    {
        const string TrackerName = LeproductionParser.TrackerName;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        /// <summary>
        /// Parse category listings. limitPage &gt; 0 limits pages per category;
        /// limitPage ≤ 0 detects last page from pagination.
        /// </summary>
        public async Task<string> ParseAsync(int limitPage = 0)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string host = AppInit.conf.Leproduction?.rqHost()?.TrimEnd('/');
                if (string.IsNullOrWhiteSpace(host))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Leproduction.host");
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

                    foreach (var kv in LeproductionCategories.Map)
                    {
                        string cat = kv.Key;
                        string[] types = kv.Value.Types;

                        int totalPages = limitPage;
                        if (totalPages <= 0)
                            totalPages = await DetectLastPageAsync(host, cat);

                        for (int page = 1; page <= totalPages; page++)
                        {
                            if (page > 1)
                                await Task.Delay(AppInit.conf.Leproduction.parseDelay);

                            string pageUrl = page == 1
                                ? $"{host}/{cat}/"
                                : $"{host}/{cat}/page/{page}/";

                            var (fetched, added, updated, skipped, failed) =
                                await ParsePageAsync(pageUrl, host, cat, types);

                            totalFetched += fetched;
                            totalAdded += added;
                            totalUpdated += updated;
                            totalSkipped += skipped;
                            totalFailed += failed;

                            ParserLog.Write(TrackerName, $"Category page done", new Dictionary<string, object>
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

        async Task<int> DetectLastPageAsync(string host, string cat)
        {
            string html = await HttpClient.Get(
                $"{host}/{cat}/",
                encoding: Encoding.UTF8,
                useproxy: AppInit.conf.Leproduction.useproxy);

            return LeproductionParser.DetectLastPage(html);
        }

        async Task<(int fetched, int added, int updated, int skipped, int failed)> ParsePageAsync(
            string pageUrl, string host, string cat, string[] types)
        {
            string html = await HttpClient.Get(
                pageUrl,
                encoding: Encoding.UTF8,
                useproxy: AppInit.conf.Leproduction.useproxy);

            if (string.IsNullOrEmpty(html))
            {
                ParserLog.Write(TrackerName, "Page fetch failed", new Dictionary<string, object>
                {
                    { "cat", cat },
                    { "url", pageUrl },
                    { "reason", "null response" }
                });
                return (0, 0, 0, 0, 0);
            }

            var postUrls = LeproductionParser.ExtractPostUrls(html, host);
            if (postUrls.Count == 0)
                return (0, 0, 0, 0, 0);

            var torrents = new List<TorrentDetails>();
            foreach (string postUrl in postUrls)
            {
                string detailHtml = await HttpClient.Get(
                    postUrl,
                    encoding: Encoding.UTF8,
                    referer: pageUrl,
                    useproxy: AppInit.conf.Leproduction.useproxy);

                if (string.IsNullOrEmpty(detailHtml))
                    continue;

                torrents.AddRange(LeproductionParser.ParseDetailHtml(detailHtml, postUrl, types));
            }

            int fetched = torrents.Count;
            int added = 0, updated = 0, skipped = 0, failed = 0;

            if (torrents.Count == 0)
                return (0, 0, 0, 0, 0);

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                bool exists = db.TryGetValue(t.url, out TorrentDetails cached);
                bool needMagnet = !exists
                    || !string.Equals(cached.title?.Trim(), t.title?.Trim(), StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(cached.magnet);

                if (needMagnet && string.IsNullOrWhiteSpace(t.magnet))
                {
                    string tid = LeproductionParser.ExtractTorrentId(t.url);
                    if (!string.IsNullOrWhiteSpace(tid))
                    {
                        string downUrl = $"{host}/index.php?do=download&id={tid}";
                        string magHtml = await HttpClient.Get(
                            downUrl,
                            encoding: Encoding.UTF8,
                            referer: host,
                            useproxy: AppInit.conf.Leproduction.useproxy);

                        if (!string.IsNullOrEmpty(magHtml))
                            t.magnet = LeproductionParser.ExtractMagnet(magHtml);
                    }
                }

                if (needMagnet && string.IsNullOrWhiteSpace(t.magnet))
                {
                    failed++;
                    ParserLog.WriteFailed(TrackerName, t, "could not get magnet");
                    return false;
                }

                if (!needMagnet)
                {
                    skipped++;
                    ParserLog.WriteSkipped(TrackerName, cached, "no changes");
                    return false;
                }

                if (exists && !string.IsNullOrWhiteSpace(cached.magnet)
                    && string.Equals(cached.magnet, t.magnet, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(cached.title?.Trim(), t.title?.Trim(), StringComparison.Ordinal))
                {
                    skipped++;
                    ParserLog.WriteSkipped(TrackerName, cached, "no changes");
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
