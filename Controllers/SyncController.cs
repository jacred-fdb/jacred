using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Tracks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Utils;
using JacRed.Models;
using JacRed.Models.Details;
using JacRed.Models.Sync.v2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Controllers
{
    public class SyncController : BaseController
    {
        public SyncController(IMemoryCache memoryCache) : base(memoryCache) { }

        static Dictionary<string, TorrentInfo> masterDbCache;

        public static void Configuration()
        {
            Console.WriteLine("SyncController load");

            masterDbCache = FileDB.masterDb.OrderBy(i => i.Value.fileTime).ToDictionary(k => k.Key, v => v.Value);

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10));
                    try { masterDbCache = FileDB.masterDb.OrderBy(i => i.Value.fileTime).ToDictionary(k => k.Key, v => v.Value); } catch { }
                }
            });
        }


        [Route("/sync/conf")]
        public JsonResult SyncConf()
        {
            return Json(new
            {
                fbd = true,
                spidr = true,
                version = 2
            });
        }

        [Route("/sync/fdb")]
        public ActionResult FdbKey(string key)
        {
            if (!AppInit.conf.opensync)
                return Content("[]", "application/json; charset=utf-8");

            return Json(FileDB.masterDb.Where(i => i.Key.Contains(key)).Take(20).Select(i => new
            {
                i.Key,
                i.Value.updateTime,
                i.Value.fileTime,
                path = $"Data/fdb/{HashTo.md5(i.Key).Substring(0, 2)}/{HashTo.md5(i.Key).Substring(2)}",
                value = FileDB.OpenRead(i.Key, cache: false)
            }).ToArray());
        }

        [Route("/sync/fdb/torrents")]
        public ActionResult FdbTorrents(long time, long start = -1, bool spidr = false)
        {
            if (!AppInit.conf.opensync || time == 0)
                return Json(new { nextread = false, collections = new List<Collection>() });

            bool nextread = false;
            int take = 2_000, countread = 0;
            var collections = new List<Collection>(take);

            foreach (var item in masterDbCache.Where(i => i.Value.fileTime > time))
            {
                var torrent = new Dictionary<string, TorrentDetails>();

                foreach (var t in FileDB.OpenRead(item.Key, cache: false))
                {
                    if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.Value.trackerName))
                        continue;

                    if (spidr || (start != -1 && start > t.Value.updateTime.ToFileTimeUtc()))
                    {
                        torrent.TryAdd(t.Key, new TorrentDetails()
                        {
                            sid = t.Value.sid,
                            pir = t.Value.pir,
                            url = t.Value.url
                        });
                        continue;
                    }

                    if (t.Value.ffprobe == null || t.Value.languages == null)
                    {
                        var streams = TracksDB.Get(t.Value.magnet, t.Value.types);
                        if (streams != null)
                        {
                            var _t = (TorrentDetails)t.Value.Clone();
                            _t.ffprobe = streams;
                            _t.languages = TracksDB.Languages(_t, streams);
                            torrent.TryAdd(t.Key, _t);
                        }
                        else
                        {
                            torrent.TryAdd(t.Key, t.Value);
                        }
                    }
                    else
                    {
                        torrent.TryAdd(t.Key, t.Value);
                    }
                }

                if (torrent.Count > 0)
                {
                    countread = countread + torrent.Count;

                    collections.Add(new Collection()
                    {
                        Key = item.Key,
                        Value = new Value()
                        {
                            time = item.Value.updateTime,
                            fileTime = item.Value.fileTime,
                            torrents = torrent
                        }
                    });
                }

                if (countread > take)
                {
                    nextread = true;
                    break;
                }
            }

            return Json(new { nextread, countread, take, collections });
        }


        [Route("/sync/torrents")]
        public JsonResult Torrents(long time)
        {
            return Json(new { error = "use GET /sync/fdb/torrents" });
        }


        [Route("/sync/tracks/stats")]
        public JsonResult TracksStats(bool includeTorrentDb = true, bool refresh = false)
        {
            if (!AppInit.conf.opensync)
                return Json(new { error = "opensync disabled" });

            var stats = TracksDB.GetExportStats(includeTorrentDb, refresh);
            return Json(new
            {
                ok = true,
                updatedAt = TracksDB.GetExportStatsUpdatedAt(),
                fromCache = TracksDB.LastExportStatsFromCache,
                stats
            });
        }
    }
}
