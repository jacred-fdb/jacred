using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Engine;
using JacRed.Engine.CORE;
using JacRed.Models.Details;
using Microsoft.AspNetCore.Mvc;
using MonoTorrent;

namespace JacRed.Controllers
{
    [Route("/dev/[action]")]
    public class DevController : Controller
    {
        public JsonResult UpdateSize()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            #region getSizeInfo
            long getSizeInfo(string sizeName)
            {
                if (string.IsNullOrWhiteSpace(sizeName))
                    return 0;

                try
                {
                    double size = 0.1;
                    var gsize = Regex.Match(sizeName, "([0-9\\.,]+) (Mb|МБ|GB|ГБ|TB|ТБ)", RegexOptions.IgnoreCase).Groups;
                    if (!string.IsNullOrWhiteSpace(gsize[2].Value))
                    {
                        if (double.TryParse(gsize[1].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out size) && size != 0)
                        {
                            if (gsize[2].Value.ToLower() is "gb" or "гб")
                                size *= 1024;

                            if (gsize[2].Value.ToLower() is "tb" or "тб")
                                size *= 1048576;

                            return (long)(size * 1048576);
                        }
                    }
                }
                catch { }

                return 0;
            }
            #endregion

            foreach (var item in FileDB.masterDb.OrderBy(i => i.Value.fileTime).ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }
                        torrent.Value.size = getSizeInfo(torrent.Value.sizeName);
                        torrent.Value.updateTime = DateTime.UtcNow;
                        FileDB.masterDb[item.Key] = new Models.TorrentInfo() { updateTime = torrent.Value.updateTime, fileTime = torrent.Value.updateTime.ToFileTimeUtc() };
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true });
        }

        public JsonResult ResetCheckTime()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }
                        torrent.Value.checkTime = DateTime.Today.AddDays(-1);
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true });
        }

        public JsonResult UpdateDetails()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }
                        FileDB.updateFullDetails(torrent.Value);
                        torrent.Value.languages = null;

                        torrent.Value.updateTime = DateTime.UtcNow;
                        FileDB.masterDb[item.Key] = new Models.TorrentInfo() { updateTime = torrent.Value.updateTime, fileTime = torrent.Value.updateTime.ToFileTimeUtc() };
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true });
        }

        public JsonResult UpdateSearchName()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    var toMigrate = new List<(string url, TorrentDetails t, string newKey)>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }
                        // Repair missing name/originalname from title so structure is valid
                        if (string.IsNullOrWhiteSpace(torrent.Value.name))
                            torrent.Value.name = torrent.Value.title ?? "";
                        if (string.IsNullOrWhiteSpace(torrent.Value.originalname))
                            torrent.Value.originalname = torrent.Value.title ?? torrent.Value.name ?? "";
                        torrent.Value._sn = StringConvert.SearchName(torrent.Value.name);
                        torrent.Value._so = StringConvert.SearchName(torrent.Value.originalname);
                        // Если ключ бакета изменился (например починили name) — переносим торрент в правильный бакет, чтобы поиск находил по новому ключу
                        string newKey = FileDB.KeyForTorrent(torrent.Value.name, torrent.Value.originalname);
                        if (!string.IsNullOrEmpty(newKey) && newKey != item.Key && newKey.IndexOf(':') > 0)
                            toMigrate.Add((torrent.Key, torrent.Value, newKey));
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);
                    foreach (var (url, t, newKey) in toMigrate)
                    {
                        fdb.Database.Remove(url);
                        FileDB.MigrateTorrentToNewKey(t, newKey);
                    }
                    if (fdb.Database.Count == 0)
                        FileDB.RemoveKeyFromMasterDb(item.Key);
                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true });
        }

        /// <summary>
        /// Scan DB for corrupt entries (null Value, missing name/originalname/trackerName). Read-only, no changes.
        /// </summary>
        public JsonResult FindCorrupt(int sampleSize = 20)
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            int totalTorrents = 0;
            int nullValueCount = 0;
            int missingNameCount = 0;
            int missingOriginalnameCount = 0;
            int missingTrackerNameCount = 0;
            var nullValueSample = new List<object>();
            var missingNameSample = new List<object>();
            var missingOriginalnameSample = new List<object>();
            var missingTrackerNameSample = new List<object>();

            foreach (var item in FileDB.masterDb.ToArray())
            {
                var db = FileDB.OpenRead(item.Key, cache: false);
                if (db == null)
                    continue;

                foreach (var kv in db)
                {
                    totalTorrents++;
                    string fdbKey = item.Key;
                    string url = kv.Key;
                    var t = kv.Value;

                    if (t == null)
                    {
                        nullValueCount++;
                        if (nullValueSample.Count < sampleSize)
                            nullValueSample.Add(new { fdbKey, url });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(t.trackerName))
                    {
                        missingTrackerNameCount++;
                        if (missingTrackerNameSample.Count < sampleSize)
                            missingTrackerNameSample.Add(new { fdbKey, url, title = t.title });
                    }
                    if (string.IsNullOrWhiteSpace(t.name))
                    {
                        missingNameCount++;
                        if (missingNameSample.Count < sampleSize)
                            missingNameSample.Add(new { fdbKey, url, title = t.title });
                    }
                    if (string.IsNullOrWhiteSpace(t.originalname))
                    {
                        missingOriginalnameCount++;
                        if (missingOriginalnameSample.Count < sampleSize)
                            missingOriginalnameSample.Add(new { fdbKey, url, title = t.title });
                    }
                }
            }

            return Json(new
            {
                ok = true,
                totalFdbKeys = FileDB.masterDb.Count,
                totalTorrents,
                corrupt = new
                {
                    nullValue = new { count = nullValueCount, sample = nullValueSample },
                    missingName = new { count = missingNameCount, sample = missingNameSample },
                    missingOriginalname = new { count = missingOriginalnameCount, sample = missingOriginalnameSample },
                    missingTrackerName = new { count = missingTrackerNameCount, sample = missingTrackerNameSample }
                }
            });
        }

        /// <summary>
        /// Remove only corrupt entries where torrent.Value == null (e.g. empty url, broken refs). No other repairs.
        /// </summary>
        public JsonResult RemoveNullValues()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            int totalRemoved = 0;
            int affectedFiles = 0;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                            keysToRemove.Add(torrent.Key);
                    }
                    if (keysToRemove.Count > 0)
                    {
                        foreach (var k in keysToRemove)
                            fdb.Database.Remove(k);
                        totalRemoved += keysToRemove.Count;
                        affectedFiles++;
                        fdb.savechanges = true;
                    }
                }
            }

            FileDB.SaveChangesToFile();
            return Json(new { ok = true, removed = totalRemoved, affectedFiles });
        }

        #region Tracks / Torrserver API test endpoints

        /// <summary>Current tracks configuration: workers, time windows, tservers count. Metadata = video/audio/subtitle streams from ffprobe.</summary>
        public IActionResult TracksConfig()
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            var c = AppInit.conf;
            var serverList = AppInit.GetTorrserverList();
            return Json(new
            {
                ok = true,
                enabled = c.tracks,
                onlyNew = c.tracksOnlyNew,
                mod = c.tracksmod,
                windows = new
                {
                    dayDays = c.tracksDayWindowDays,
                    monthDays = c.tracksMonthWindowDays,
                    yearMonths = c.tracksYearWindowMonths,
                    updatesDays = c.tracksUpdatesWindowDays
                },
                workers = new
                {
                    day = c.tracksWorkersDay,
                    month = c.tracksWorkersMonth,
                    year = c.tracksWorkersYear,
                    older = c.tracksWorkersOlder,
                    updates = c.tracksWorkersUpdates
                },
                tserversCount = serverList?.Count ?? 0,
                timeoutMinutes = c.tracksFfpTimeoutMinutes,
                metadataPollMs = c.tracksMetadataPollMs,
                metadataMaxAttempts = c.tracksMetadataMaxAttempts
            });
        }

        /// <summary>Queue one-time metadata run for torrents without metadata in last N days. window=1 (day), 7 (week), 30 (month). Only from localhost.</summary>
        public IActionResult TracksRunOnce(int window = 7)
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            var serverList = AppInit.GetTorrserverList();
            if (serverList == null || serverList.Count == 0)
                return Json(new { error = "tservers not configured or empty" });

            int windowDays = Math.Max(1, Math.Min(365, window));
            _ = System.Threading.Tasks.Task.Run(() => TracksCron.RunOnce(windowDays));
            return Json(new { ok = true, queued = true, windowDays, message = "One-time metadata run queued for torrents (no metadata yet) created in last " + windowDays + " days." });
        }

        /// <summary>Run full track pipeline: add → wait metadata → ffp → rem. Use magnet= or hash= (hash = infohash, then magnet must be in DB or pass magnet).</summary>
        public async System.Threading.Tasks.Task<IActionResult> TracksTest(string magnet, string hash)
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            string infohash = null;
            string magnetToUse = magnet;
            if (!string.IsNullOrWhiteSpace(hash))
            {
                infohash = hash.Trim().ToLowerInvariant();
                if (infohash.Length != 40)
                    return Json(new { error = "hash must be 40-char infohash" });
                if (string.IsNullOrWhiteSpace(magnetToUse))
                    magnetToUse = "magnet:?xt=urn:btih:" + infohash;
            }
            else if (!string.IsNullOrWhiteSpace(magnetToUse))
            {
                try
                {
                    infohash = MagnetLink.Parse(magnetToUse).InfoHashes.V1OrV2.ToHex();
                }
                catch
                {
                    return Json(new { error = "invalid magnet" });
                }
                if (string.IsNullOrEmpty(infohash) || infohash.Length != 40)
                    return Json(new { error = "invalid magnet: infohash must be 40 chars" });
            }
            else
                return Json(new { error = "pass magnet= or hash=" });

            var serverList = AppInit.GetTorrserverList();
            if (serverList == null || serverList.Count == 0)
                return Json(new { error = "tservers not configured or empty" });

            var (tsuri, tsuser, tspass) = serverList[new Random().Next(0, serverList.Count)];
            string addResp = await TorrserverClient.AddTorrent(tsuri, magnetToUse, 120, tsuser, tspass);
            if (string.IsNullOrEmpty(addResp))
                return Json(new { ok = false, step = "add", error = "add failed" });

            int pollMs = Math.Max(500, Math.Min(30000, AppInit.conf.tracksMetadataPollMs));
            int maxAttempts = Math.Max(5, Math.Min(300, AppInit.conf.tracksMetadataMaxAttempts));
            Models.Tracks.TorrserverTorrentStatus status = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                await System.Threading.Tasks.Task.Delay(pollMs);
                status = await TorrserverClient.GetTorrent(tsuri, infohash, 20, tsuser, tspass);
                if (TorrserverClient.HasMetadata(status))
                    break;
            }

            if (!TorrserverClient.HasMetadata(status))
            {
                await TorrserverClient.RemTorrent(tsuri, infohash, 15, tsuser, tspass);
                return Json(new { ok = false, step = "metadata", error = "metadata timeout" });
            }

            var ffp = await TorrserverClient.Ffp(tsuri, infohash, 1, 180, tsuser, tspass);
            await TorrserverClient.RemTorrent(tsuri, infohash, 15, tsuser, tspass);

            return Json(new
            {
                ok = true,
                infohash,
                tsuri,
                streams_count = ffp?.streams?.Count ?? 0,
                streams = ffp?.streams
            });
        }

        /// <summary>GET /ffp/{hash}/{index} from a configured Torrserver. Torrent must already be on server.</summary>
        public async System.Threading.Tasks.Task<IActionResult> TracksFfp(string hash, int index = 1)
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });

            string infohash = (hash ?? "").Trim().ToLowerInvariant();
            if (infohash.Length != 40)
                return Json(new { error = "hash must be 40-char infohash" });

            var serverList = AppInit.GetTorrserverList();
            if (serverList == null || serverList.Count == 0)
                return Json(new { error = "tservers not configured or empty" });

            var (tsuri, tsuser, tspass) = serverList[0];
            var ffp = await TorrserverClient.Ffp(tsuri, infohash, index, 120, tsuser, tspass);
            if (ffp?.streams == null)
                return Json(new { ok = false, error = "ffp failed or no streams" });
            return Json(new { ok = true, streams_count = ffp.streams.Count, streams = ffp.streams });
        }

        /// <summary>Add torrent to first Torrserver. POST body or query: link (magnet).</summary>
        public async System.Threading.Tasks.Task<IActionResult> TracksTorrentAdd(string link)
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });
            var serverList = AppInit.GetTorrserverList();
            if (serverList == null || serverList.Count == 0)
                return Json(new { error = "tservers not configured or empty" });
            string magnet = link?.Trim();
            if (string.IsNullOrEmpty(magnet))
                return Json(new { error = "link (magnet) required" });
            var (tsuri, tsuser, tspass) = serverList[0];
            string resp = await TorrserverClient.AddTorrent(tsuri, magnet, 120, tsuser, tspass);
            if (string.IsNullOrEmpty(resp))
                return Json(new { ok = false, error = "add failed" });
            return Json(new { ok = true, response = resp });
        }

        /// <summary>Get torrent status from first Torrserver. hash = infohash.</summary>
        public async System.Threading.Tasks.Task<IActionResult> TracksTorrentGet(string hash)
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });
            string infohash = (hash ?? "").Trim().ToLowerInvariant();
            if (infohash.Length != 40)
                return Json(new { error = "hash must be 40-char infohash" });
            var serverList = AppInit.GetTorrserverList();
            if (serverList == null || serverList.Count == 0)
                return Json(new { error = "tservers not configured or empty" });
            var (tsuri, tsuser, tspass) = serverList[0];
            var status = await TorrserverClient.GetTorrent(tsuri, infohash, 20, tsuser, tspass);
            if (status == null)
                return Json(new { ok = false, error = "not found" });
            return Json(new { ok = true, has_metadata = TorrserverClient.HasMetadata(status), status });
        }

        /// <summary>Remove torrent from first Torrserver. hash = infohash.</summary>
        public async System.Threading.Tasks.Task<IActionResult> TracksTorrentRem(string hash)
        {
            if (HttpContext.Connection.RemoteIpAddress?.ToString() != "127.0.0.1")
                return Json(new { badip = true });
            string infohash = (hash ?? "").Trim().ToLowerInvariant();
            if (infohash.Length != 40)
                return Json(new { error = "hash must be 40-char infohash" });
            var serverList = AppInit.GetTorrserverList();
            if (serverList == null || serverList.Count == 0)
                return Json(new { error = "tservers not configured or empty" });
            var (tsuri, tsuser, tspass) = serverList[0];
            await TorrserverClient.RemTorrent(tsuri, infohash, 15, tsuser, tspass);
            return Json(new { ok = true });
        }

        #endregion
    }
}
