using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Anidub
{
    public class AnidubSyncService
    {
        const string TrackerName = "anidub";

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        public async Task<string> ParseAsync(int parseFrom = 0, int parseTo = 0)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = AppInit.conf.Anidub.host;

                    int startPage = parseFrom > 0 ? parseFrom : 1;
                    int endPage = parseTo > 0 ? parseTo : (parseFrom > 0 ? parseFrom : 1);

                    if (startPage > endPage)
                    {
                        int temp = startPage;
                        startPage = endPage;
                        endPage = temp;
                    }

                    ParserLog.Write(TrackerName, $"Starting parse", new Dictionary<string, object>
                    {
                        { "parseFrom", parseFrom },
                        { "parseTo", parseTo },
                        { "startPage", startPage },
                        { "endPage", endPage },
                        { "baseUrl", baseUrl }
                    });

                    int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                    for (int page = startPage; page <= endPage; page++)
                    {
                        if (page > startPage)
                            await Task.Delay(AppInit.conf.Anidub.parseDelay);

                        if (page > 1)
                        {
                            ParserLog.Write(TrackerName, $"Parsing page", new Dictionary<string, object>
                            {
                                { "page", page },
                                { "url", $"{baseUrl}/page/{page}/" }
                            });
                        }

                        (int parsed, int added, int updated, int skipped, int failed) = await parsePage(page);
                        totalParsed += parsed;
                        totalAdded += added;
                        totalUpdated += updated;
                        totalSkipped += skipped;
                        totalFailed += failed;
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
                    ParserLog.Write(TrackerName, $"Canceled", new Dictionary<string, object>
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

                    ParserLog.Write(TrackerName, $"Error", new Dictionary<string, object>
                    {
                        { "message", ex.Message },
                        { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                    });
                }

                return "ok";
            });
        }

        async Task<(int parsed, int added, int updated, int skipped, int failed)> parsePage(int page)
        {
            string url = page == 1 ? AppInit.conf.Anidub.host : $"{AppInit.conf.Anidub.host}/page/{page}/";
            string html = await HttpClient.Get(url, encoding: Encoding.UTF8, useproxy: AppInit.conf.Anidub.useproxy);

            if (html == null || !html.Contains(AnidubParser.ValidationDleContent))
            {
                ParserLog.Write(TrackerName, $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", url },
                    { "reason", html == null ? "null response" : "invalid content" }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = AnidubParser.ParseTorrentListFromHtml(html, AppInit.conf.Anidub.host, page);

            int parsedCount = torrents.Count;
            int addedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, async (t, db) =>
                {
                    bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);
                    string detailHtml = null;

                    if (exists && string.Equals(_tcache.title?.Trim(), t.title?.Trim(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_tcache.magnet))
                    {
                        detailHtml = await HttpClient.Get(t.url, encoding: Encoding.UTF8, useproxy: AppInit.conf.Anidub.useproxy);
                        if (detailHtml != null)
                        {
                            int relased = AnidubParser.ExtractRelased(detailHtml);
                            if (relased > 0)
                                t.relased = relased;

                            var magnetMatch = Regex.Match(detailHtml, "href=\"(magnet:\\?[^\"]+)\"", RegexOptions.IgnoreCase);
                            if (magnetMatch.Success)
                            {
                                string currentMagnet = magnetMatch.Groups[1].Value;
                                bool shouldSkip = string.Equals(_tcache.magnet, currentMagnet, StringComparison.OrdinalIgnoreCase);

                                if (shouldSkip && _tcache.relased == 0 && relased > 0)
                                {
                                    t.magnet = currentMagnet;
                                    t.relased = relased;

                                    var sizeMatchLocal = Regex.Match(detailHtml, "Размер[^:]*:\\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                                    if (!sizeMatchLocal.Success)
                                        sizeMatchLocal = Regex.Match(detailHtml, "Размер[^:]*:\\s*([^<]+)", RegexOptions.IgnoreCase);
                                    if (sizeMatchLocal.Success)
                                        t.sizeName = HttpUtility.HtmlDecode(sizeMatchLocal.Groups[1].Value).Trim();

                                    updatedCount++;
                                    ParserLog.WriteUpdated(TrackerName, t, "relased updated");
                                    return true;
                                }

                                if (shouldSkip)
                                {
                                    skippedCount++;
                                    ParserLog.WriteSkipped(TrackerName, _tcache, "no changes");
                                    return false;
                                }

                                t.magnet = currentMagnet;

                                var sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                                if (!sizeMatch.Success)
                                    sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*([^<]+)", RegexOptions.IgnoreCase);
                                if (sizeMatch.Success)
                                    t.sizeName = HttpUtility.HtmlDecode(sizeMatch.Groups[1].Value).Trim();

                                var downloadMatch = Regex.Match(detailHtml, "href=\"([^\"]*engine/download\\.php\\?id=[0-9]+)\"", RegexOptions.IgnoreCase);
                                if (downloadMatch.Success)
                                {
                                    string downloadUrl = downloadMatch.Groups[1].Value;
                                    if (!downloadUrl.StartsWith("http"))
                                        downloadUrl = $"{AppInit.conf.Anidub.host}/{downloadUrl.TrimStart('/')}";

                                    byte[] torrentFile = await HttpClient.Download(downloadUrl, referer: t.url, useproxy: AppInit.conf.Anidub.useproxy);
                                    if (torrentFile != null && torrentFile.Length > 0)
                                    {
                                        string sizeName = BencodeTo.SizeName(torrentFile);
                                        if (!string.IsNullOrWhiteSpace(sizeName))
                                            t.sizeName = sizeName;
                                    }
                                }

                                updatedCount++;
                                ParserLog.WriteUpdated(TrackerName, t, "magnet changed");
                                return true;
                            }
                        }
                    }

                    if (detailHtml == null)
                    {
                        byte[] torrent = await HttpClient.Download(t.downloadUri, referer: AppInit.conf.Anidub.host);

                        if (torrent != null && torrent.Length > 0)
                        {
                            string magnet = BencodeTo.Magnet(torrent);
                            string sizeName = BencodeTo.SizeName(torrent);

                            if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
                            {
                                t.magnet = magnet;
                                t.sizeName = sizeName;

                                if (exists)
                                {
                                    updatedCount++;
                                    ParserLog.WriteUpdated(TrackerName, t, "magnet from downloadUri");
                                }
                                else
                                {
                                    addedCount++;
                                    ParserLog.WriteAdded(TrackerName, t);
                                }
                                return true;
                            }
                        }
                    }

                    if (detailHtml == null)
                    {
                        detailHtml = await HttpClient.Get(t.url, encoding: Encoding.UTF8, useproxy: AppInit.conf.Anidub.useproxy);
                    }

                    if (detailHtml != null)
                    {
                        int relased = AnidubParser.ExtractRelased(detailHtml);
                        if (relased > 0)
                            t.relased = relased;

                        var magnetMatch = Regex.Match(detailHtml, "href=\"(magnet:\\?[^\"]+)\"", RegexOptions.IgnoreCase);
                        if (magnetMatch.Success)
                        {
                            t.magnet = magnetMatch.Groups[1].Value;

                            var sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                            if (!sizeMatch.Success)
                                sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*([^<]+)", RegexOptions.IgnoreCase);
                            string sizeName = sizeMatch.Success ? HttpUtility.HtmlDecode(sizeMatch.Groups[1].Value).Trim() : null;
                            if (sizeMatch.Success)
                                t.sizeName = sizeName;

                            if (exists)
                            {
                                updatedCount++;
                                ParserLog.WriteUpdated(TrackerName, t, "magnet from detail page");
                            }
                            else
                            {
                                addedCount++;
                                ParserLog.WriteAdded(TrackerName, t);
                            }
                            return true;
                        }

                        var downloadMatch = Regex.Match(detailHtml, "href=\"([^\"]*engine/download\\.php\\?id=[0-9]+)\"", RegexOptions.IgnoreCase);
                        if (downloadMatch.Success)
                        {
                            string downloadUrl = downloadMatch.Groups[1].Value;
                            if (!downloadUrl.StartsWith("http"))
                                downloadUrl = $"{AppInit.conf.Anidub.host}/{downloadUrl.TrimStart('/')}";

                            byte[] torrentFile = await HttpClient.Download(downloadUrl, referer: t.url, useproxy: AppInit.conf.Anidub.useproxy);
                            if (torrentFile != null && torrentFile.Length > 0)
                            {
                                string magnet = BencodeTo.Magnet(torrentFile);
                                string sizeName = BencodeTo.SizeName(torrentFile);

                                if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
                                {
                                    t.magnet = magnet;
                                    t.sizeName = sizeName;

                                    if (t.relased == 0 && relased > 0)
                                    {
                                        t.relased = relased;
                                    }

                                    if (exists)
                                    {
                                        updatedCount++;
                                        ParserLog.WriteUpdated(TrackerName, t, "magnet from torrent file");
                                    }
                                    else
                                    {
                                        addedCount++;
                                        ParserLog.WriteAdded(TrackerName, t);
                                    }
                                    return true;
                                }
                            }

                            var sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                            if (!sizeMatch.Success)
                                sizeMatch = Regex.Match(detailHtml, "Размер[^:]*:\\s*([^<]+)", RegexOptions.IgnoreCase);
                            if (sizeMatch.Success)
                                t.sizeName = HttpUtility.HtmlDecode(sizeMatch.Groups[1].Value).Trim();
                        }
                    }

                    failedCount++;
                    ParserLog.WriteFailed(TrackerName, t, "could not get magnet or size");
                    return false;
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

            return (parsedCount, addedCount, updatedCount, skippedCount, failedCount);
        }
    }
}
