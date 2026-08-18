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

        static string RequestHost() => (AppInit.conf.Anistar?.rqHost() ?? "").TrimEnd('/');

        static string CookieOrNull()
            => string.IsNullOrWhiteSpace(AppInit.conf.Anistar?.cookie) ? null : AppInit.conf.Anistar.cookie;

        public async Task<string> ParseAsync(int limitPage = 0)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    string host = RequestHost();
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        ParserLog.Write(TrackerName, "Config missing", new Dictionary<string, object> { { "reason", "empty host" } });
                        return "config missing";
                    }

                    string cookie = CookieOrNull();
                    bool cookieSet = cookie != null;

                    ParserLog.Write(TrackerName, "Starting parse", new Dictionary<string, object>
                    {
                        { "limitPage", limitPage },
                        { "host", host },
                        { "cookieSet", cookieSet }
                    });

                    int totalFetched = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;
                    int emptyPages = 0;

                    foreach (var kv in AnistarCategories.Map)
                    {
                        string catPath = kv.Key;
                        string[] types = kv.Value.Types;
                        int lastPage = limitPage;
                        if (lastPage <= 0)
                        {
                            string firstHtml = await HttpClient.Get($"{host}/{catPath}/", encoding: PageEncoding, cookie: cookie, useproxy: AppInit.conf.Anistar.useproxy);
                            lastPage = AnistarParser.DetectLastPage(firstHtml);
                        }

                        for (int page = 1; page <= lastPage; page++)
                        {
                            string listUrl = page <= 1
                                ? $"{host}/{catPath}/"
                                : $"{host}/{catPath}/page/{page}/";

                            ParserLog.Write(TrackerName, "Parsing list page", new Dictionary<string, object>
                            {
                                { "category", catPath },
                                { "page", page },
                                { "url", listUrl }
                            });

                            string listHtml = await HttpClient.Get(listUrl, encoding: PageEncoding, cookie: cookie, referer: host + "/", useproxy: AppInit.conf.Anistar.useproxy);
                            if (!TryUseListHtml(listHtml, listUrl, catPath, page, cookieSet, out var postUrls))
                            {
                                emptyPages++;
                                continue;
                            }

                            totalFetched += postUrls.Count;

                            foreach (string postUrl in postUrls)
                            {
                                var (added, updated, skipped, failed) = await ParseDetailAndSave(postUrl, listUrl, host, types, cookie, cookieSet);
                                totalAdded += added;
                                totalUpdated += updated;
                                totalSkipped += skipped;
                                totalFailed += failed;

                                if (AppInit.conf.Anistar.parseDelay > 0)
                                    await Task.Delay(AppInit.conf.Anistar.parseDelay);
                            }
                        }
                    }

                    bool noResults = totalFetched == 0;
                    ParserLog.Write(TrackerName, noResults
                            ? $"Parse completed with no results (took {sw.Elapsed.TotalSeconds:F1}s)"
                            : $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                        new Dictionary<string, object>
                        {
                            { "fetched", totalFetched },
                            { "added", totalAdded },
                            { "updated", totalUpdated },
                            { "skipped", totalSkipped },
                            { "failed", totalFailed },
                            { "emptyPages", emptyPages }
                        });

                    return noResults ? "empty" : "ok";
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

        static bool TryUseListHtml(string listHtml, string listUrl, string catPath, int page, bool cookieSet, out List<string> postUrls)
        {
            postUrls = null;
            if (string.IsNullOrEmpty(listHtml))
            {
                ParserLog.Write(TrackerName, "Page fetch failed", new Dictionary<string, object>
                {
                    { "category", catPath },
                    { "page", page },
                    { "url", listUrl },
                    { "htmlLength", 0 },
                    { "cookieSet", cookieSet },
                    { "reason", "null response" }
                });
                return false;
            }

            if (CloudflareClearance.IsChallengeBody(listHtml))
            {
                ParserLog.Write(TrackerName, "Page fetch failed", new Dictionary<string, object>
                {
                    { "category", catPath },
                    { "page", page },
                    { "url", listUrl },
                    { "htmlLength", listHtml.Length },
                    { "cookieSet", cookieSet },
                    { "reason", "cloudflare challenge" }
                });
                return false;
            }

            postUrls = AnistarParser.ExtractPostUrls(listHtml, RequestHost());
            if (postUrls.Count == 0)
            {
                ParserLog.Write(TrackerName, "No posts extracted", new Dictionary<string, object>
                {
                    { "category", catPath },
                    { "page", page },
                    { "url", listUrl },
                    { "htmlLength", listHtml.Length },
                    { "cookieSet", cookieSet },
                    { "hasDleContent", listHtml.IndexOf("dle-content", StringComparison.OrdinalIgnoreCase) >= 0 }
                });
                return false;
            }

            return true;
        }

        async Task<(int added, int updated, int skipped, int failed)> ParseDetailAndSave(string postUrl, string referer, string host, string[] types, string cookie, bool cookieSet)
        {
            string postHtml = await HttpClient.Get(postUrl, encoding: PageEncoding, cookie: cookie, referer: referer, useproxy: AppInit.conf.Anistar.useproxy);
            if (string.IsNullOrEmpty(postHtml) || CloudflareClearance.IsChallengeBody(postHtml))
            {
                ParserLog.Write(TrackerName, "Detail fetch failed", new Dictionary<string, object>
                {
                    { "url", postUrl },
                    { "htmlLength", postHtml?.Length ?? 0 },
                    { "cookieSet", cookieSet },
                    { "reason", string.IsNullOrEmpty(postHtml) ? "null response" : "cloudflare challenge" }
                });
                return (0, 0, 0, 1);
            }

            var torrents = AnistarParser.ParseDetailTorrents(postHtml, postUrl, types);
            if (torrents.Count == 0)
            {
                ParserLog.Write(TrackerName, "No torrents extracted", new Dictionary<string, object>
                {
                    { "url", postUrl },
                    { "htmlLength", postHtml.Length }
                });
                return (0, 0, 0, 0);
            }

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
                    byte[] torrentFile = await HttpClient.Download(downUrl, cookie: cookie, referer: host, useproxy: AppInit.conf.Anistar.useproxy);
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
