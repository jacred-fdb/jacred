using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Engine;
using JacRed.Engine.CORE;
using Microsoft.AspNetCore.Mvc;

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
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

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
    }
}
