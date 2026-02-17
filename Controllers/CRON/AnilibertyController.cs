using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using JacRed.Engine;
using JacRed.Models.Details;
using JacRed.Models.tParse;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/aniliberty/[action]")]
    public class AnilibertyController : BaseController
    {

        #region Parse
        static volatile bool workParse = false;
        private static readonly object workParseLock = new object();

        /// <summary>
        /// Attempts to start a parse operation. Returns true if successful, false if parsing is already in progress.
        /// </summary>
        /// <returns>True if parse was started, false if already running.</returns>
        private static bool TryStartParse()
        {
            lock (workParseLock)
            {
                if (workParse)
                    return false;

                workParse = true;
                return true;
            }
        }

        /// <summary>
        /// Ends the parse operation, allowing a new parse to start.
        /// </summary>
        private static void EndParse()
        {
            lock (workParseLock)
            {
                workParse = false;
            }
        }

        /// <summary>
        /// Parses torrent releases from Aniliberty API.
        /// </summary>
        /// <param name="parseFrom">The starting page number to parse from. If 0 or less, defaults to page 1.</param>
        /// <param name="parseTo">The ending page number to parse to. If 0 or less, defaults to the same value as parseFrom.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a status string:
        /// - "work" if parsing is already in progress
        /// - "canceled" if the operation was canceled
        /// - "ok" if parsing completed successfully
        /// </returns>
        [HttpGet]
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            if (!TryStartParse())
                return "work";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = AppInit.conf.Aniliberty.host;

                // Determine page range
                int startPage = parseFrom > 0 ? parseFrom : 1;
                int endPage = parseTo > 0 ? parseTo : (parseFrom > 0 ? parseFrom : 1);

                // Ensure startPage <= endPage
                if (startPage > endPage)
                {
                    int temp = startPage;
                    startPage = endPage;
                    endPage = temp;
                }

                ParserLog.Write("aniliberty", $"Starting parse", new Dictionary<string, object>
                {
                    { "parseFrom", parseFrom },
                    { "parseTo", parseTo },
                    { "startPage", startPage },
                    { "endPage", endPage },
                    { "baseUrl", baseUrl }
                });

                int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;
                int lastPage = int.MaxValue;

                // Parse pages from startPage to endPage
                for (int page = startPage; page <= endPage && page <= lastPage; page++)
                {
                    if (page > startPage)
                        await Task.Delay(AppInit.conf.Aniliberty.parseDelay);

                    ParserLog.Write("aniliberty", $"Parsing page", new Dictionary<string, object>
                    {
                        { "page", page },
                        { "url", $"{baseUrl}/api/v1/anime/torrents?page={page}&limit=50" }
                    });

                    var result = await parsePage(page);
                    totalParsed += result.parsed;
                    totalAdded += result.added;
                    totalUpdated += result.updated;
                    totalSkipped += result.skipped;
                    totalFailed += result.failed;

                    // Update lastPage from API response
                    if (result.lastPage > 0)
                        lastPage = result.lastPage;

                    // If we've reached the last page, stop
                    if (page >= lastPage)
                        break;
                }

                ParserLog.Write("aniliberty", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
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
                ParserLog.Write("aniliberty", $"Canceled", new Dictionary<string, object>
                {
                    { "message", oce.Message },
                    { "stackTrace", oce.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
                return "canceled";
            }
            catch (Exception ex)
            {
                // Rethrow critical exceptions that should never be swallowed
                if (ex is OutOfMemoryException)
                    throw;

                ParserLog.Write("aniliberty", $"Error", new Dictionary<string, object>
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
        #endregion

        #region parsePage
        /// <summary>
        /// Parses a single page of torrent releases from the Aniliberty API.
        /// </summary>
        /// <param name="page">The page number to parse.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a tuple with parsing statistics:
        /// - parsed: Total number of torrent releases found and processed
        /// - added: Number of new torrent releases added to the database
        /// - updated: Number of existing torrent releases that were updated
        /// - skipped: Number of torrent releases skipped (no changes detected)
        /// - failed: Number of torrent releases that failed to process
        /// - lastPage: Last page number from API response
        /// </returns>
        async Task<(int parsed, int added, int updated, int skipped, int failed, int lastPage)> parsePage(int page)
        {
            string url = $"{AppInit.conf.Aniliberty.host}/api/v1/anime/torrents?page={page}&limit=50";
            var response = await HttpClient.Get<AnilibertyApiResponse>(url, encoding: Encoding.UTF8, useproxy: AppInit.conf.Aniliberty.useproxy);

            if (response == null || response.Data == null || response.Data.Count == 0)
            {
                ParserLog.Write("aniliberty", $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", url },
                    { "reason", response == null ? "null response" : "no data" }
                });
                return (0, 0, 0, 0, 0, 0);
            }

            var torrents = new List<TorrentDetails>();
            int lastPage = response.Meta?.LastPage ?? 0;

            foreach (var apiTorrent in response.Data)
            {
                if (string.IsNullOrWhiteSpace(apiTorrent.Magnet) || apiTorrent.Release == null)
                {
                    ParserLog.WriteFailed("aniliberty", null, $"Missing magnet or release data for torrent {apiTorrent.Id}");
                    continue;
                }

                var release = apiTorrent.Release;
                string name = release.Name?.Main?.Trim();
                string originalname = release.Name?.English?.Trim();

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(originalname))
                {
                    ParserLog.WriteFailed("aniliberty", null, $"Missing name for torrent {apiTorrent.Id}");
                    continue;
                }

                // Extract quality info from label (e.g., "[WEBRip 1080p][HEVC][1-12]")
                string qualityInfo = ExtractQualityInfo(apiTorrent.Label);

                // Build base title from names: prefer "name / originalname" when both are present and different,
                // otherwise fall back to the first non-empty name, then to "Unknown" as a last resort.
                string baseTitle;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(originalname) && !string.Equals(name, originalname, StringComparison.Ordinal))
                {
                    baseTitle = $"{name} / {originalname}";
                }
                else if (!string.IsNullOrWhiteSpace(name))
                {
                    baseTitle = name;
                }
                else if (!string.IsNullOrWhiteSpace(originalname))
                {
                    baseTitle = originalname;
                }
                else
                {
                    baseTitle = "Unknown";
                }
                // Append year and quality info
                string title = baseTitle;

                if (release.Year.HasValue)
                    title += $" / {release.Year.Value}";

                if (!string.IsNullOrWhiteSpace(qualityInfo))
                    title += $" / {qualityInfo}";

                // Determine types
                string[] types = DetermineTypes(release.Type?.Value);

                // Parse dates
                DateTime createTime = default;
                DateTime parsedDate;
                if (!string.IsNullOrWhiteSpace(apiTorrent.CreatedAt) &&
                    DateTime.TryParse(apiTorrent.CreatedAt, out parsedDate))
                {
                    createTime = parsedDate.ToUniversalTime();
                }

                if (createTime == default)
                    createTime = DateTime.UtcNow;

                DateTime updateTime = default;
                if (!string.IsNullOrWhiteSpace(apiTorrent.UpdatedAt) &&
                    DateTime.TryParse(apiTorrent.UpdatedAt, out parsedDate))
                {
                    updateTime = parsedDate.ToUniversalTime();
                }

                if (updateTime == default)
                    updateTime = createTime;

                // Build URL: use release page URL for user-facing links, but append hash as query parameter for uniqueness
                string baseUrl = !string.IsNullOrWhiteSpace(release.Alias)
                    ? $"{AppInit.conf.Aniliberty.host}/anime/releases/release/{release.Alias}"
                    : $"{AppInit.conf.Aniliberty.host}/api/v1/anime/torrents/{apiTorrent.Hash}";

                // Append hash as query parameter to ensure uniqueness for FileDB while keeping user-friendly URL
                string torrentUrl = $"{baseUrl}?hash={apiTorrent.Hash}";

                // Format size - FileDB expects format: "Mb|МБ|GB|ГБ|TB|ТБ"
                string sizeName = FormatSize(apiTorrent.Size);

                // Parse quality from quality.value (e.g., "1080p" -> 1080)
                int quality = ParseQuality(apiTorrent.Quality?.Value);

                // Extract videotype from type.value (WEBRip, BDRip, etc.)
                string videotype = apiTorrent.Type?.Value?.ToLowerInvariant();

                torrents.Add(new TorrentDetails()
                {
                    trackerName = "aniliberty",
                    types = types,
                    url = torrentUrl,
                    title = title,
                    sid = apiTorrent.Seeders,
                    pir = apiTorrent.Leechers,
                    createTime = createTime,
                    updateTime = updateTime,
                    name = name,
                    originalname = originalname,
                    relased = release.Year ?? 0,
                    magnet = apiTorrent.Magnet,
                    sizeName = sizeName,
                    // size will be calculated by FileDB.updateFullDetails() from sizeName
                    quality = quality,
                    videotype = videotype
                });
            }

            int parsedCount = torrents.Count;
            int addedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            // If we found torrents, process them
            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, (t, db) =>
                {
                    // Check if already exists
                    bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);

                    // If torrent exists with same magnet, skip
                    if (exists && string.Equals(_tcache.magnet?.Trim(), t.magnet?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        skippedCount++;
                        ParserLog.WriteSkipped("aniliberty", _tcache, "no changes");
                        return Task.FromResult(false); // Skip processing this torrent
                    }

                    // If magnet changed or new torrent, update/add
                    if (exists)
                    {
                        updatedCount++;
                        ParserLog.WriteUpdated("aniliberty", t, "magnet changed or updated");
                    }
                    else
                    {
                        addedCount++;
                        ParserLog.WriteAdded("aniliberty", t);
                    }

                    return Task.FromResult(true);
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write("aniliberty", $"Page {page} completed",
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

        /// <summary>
        /// Determines the types array based on release type value from API.
        /// API returns English enum values: TV, MOVIE, OVA, ONA, SPECIAL, WEB, OAD, DORAMA
        /// </summary>
        /// <param name="typeValue">The release type value from API (e.g., "TV", "MOVIE", "OVA")</param>
        /// <returns>Array of type strings for the torrent</returns>
        private string[] DetermineTypes(string typeValue)
        {
            if (string.IsNullOrWhiteSpace(typeValue))
                return new string[] { "anime" };

            string typeUpper = typeValue.ToUpperInvariant();

            // Map API enum values to our types
            switch (typeUpper)
            {
                case "MOVIE":
                    return new string[] { "anime", "movie" };
                case "OVA":
                case "OAD":
                    return new string[] { "anime", "ova" };
                case "SPECIAL":
                    return new string[] { "anime", "special" };
                case "ONA":
                case "WEB":
                    return new string[] { "anime", "ona" };
                case "DORAMA":
                    return new string[] { "dorama" };
                case "TV":
                default:
                    return new string[] { "anime", "serial" }; // Default to serial/TV series
            }
        }

        /// <summary>
        /// Formats size in bytes to human-readable string.
        /// FileDB expects format matching regex: "([0-9\\.,]+) (Mb|МБ|GB|ГБ|TB|ТБ)"
        /// Uses "Mb" (lowercase b) for megabytes, "GB"/"TB" (uppercase B) for larger sizes
        /// </summary>
        private string FormatSize(long bytes)
        {
            // FileDB's getSizeInfo expects: "Mb|МБ|GB|ГБ|TB|ТБ"
            // For sizes < 1GB, use Mb; for >= 1GB use GB; for >= 1TB use TB
            if (bytes < 1073741824L) // < 1 GB
                return $"{bytes / 1048576.0:F2} Mb";
            if (bytes < 1099511627776L) // < 1 TB
                return $"{bytes / 1073741824.0:F2} GB";
            return $"{bytes / 1099511627776.0:F2} TB";
        }

        /// <summary>
        /// Parses quality string (e.g., "1080p", "720p", "4k") to integer value.
        /// </summary>
        private int ParseQuality(string qualityValue)
        {
            if (string.IsNullOrWhiteSpace(qualityValue))
                return 480; // Default

            string q = qualityValue.ToLowerInvariant().Trim();

            // Handle 4K/UHD
            if (q.Contains("4k") || q.Contains("2160p") || q.Contains("uhd"))
                return 2160;

            // Extract numeric value (e.g., "1080p" -> 1080)
            var match = System.Text.RegularExpressions.Regex.Match(q, @"(\d{3,4})p?");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int quality))
            {
                // Normalize common values
                if (quality >= 2160)
                    return 2160;
                else if (quality >= 1080)
                    return 1080;
                else if (quality >= 720)
                    return 720;
                else if (quality >= 480)
                    return 480;
                else
                    return quality;
            }

            return 480; // Default
        }

        /// <summary>
        /// Extracts quality information from label string.
        /// Example: "Tensei Kizoku - AniLibria.TOP [WEBRip 1080p][HEVC][1-12]" -> "[WEBRip 1080p][HEVC][1-12]"
        /// </summary>
        /// <param name="label">The label string from API</param>
        /// <returns>Quality info string with brackets, or empty string if not found</returns>
        private string ExtractQualityInfo(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return string.Empty;

            // Match pattern: [something] [something] ... at the end of the string
            // Example: "Title - AniLibria.TOP [WEBRip 1080p][HEVC][1-12]"
            // The pattern matches one or more bracket groups at the end
            var match = System.Text.RegularExpressions.Regex.Match(label, @"(\[[^\]]+\](?:\s*\[[^\]]+\])*)\s*$");
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            return string.Empty;
        }
        #endregion
    }
}
