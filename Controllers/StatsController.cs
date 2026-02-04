using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Engine;
using JacRed.Models.Details;

namespace JacRed.Controllers
{
    [Route("/stats/[action]")]
    public class StatsController : Controller
    {
        const string StatsPath = "Data/temp/stats.json";

        /// <summary>
        /// Список раздач по всем трекерам из БД — для сверки с тем, что на трекере. newtoday=1, updatedtoday=1, limit (по умолчанию 200).
        /// </summary>
        [Route("/stats/trackers")]
        public ActionResult Trackers(int newtoday = 0, int updatedtoday = 0, int limit = 200)
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            var list = CollectTorrents(trackerName: null, newtoday, updatedtoday, limit);
            return Json(list);
        }

        /// <summary>
        /// Без параметров — сводка из Data/temp/stats.json (newtor, update, check, alltorrents по трекерам).
        /// С trackerName — список раздач этого трекера. newtoday=1, updatedtoday=1, limit (по умолчанию 200).
        /// </summary>
        public ActionResult Torrents(string trackerName, int newtoday = 0, int updatedtoday = 0, int limit = 200)
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            if (string.IsNullOrWhiteSpace(trackerName))
            {
                if (!System.IO.File.Exists(StatsPath))
                    return Content("[]", "application/json");
                try
                {
                    return Content(System.IO.File.ReadAllText(StatsPath), "application/json");
                }
                catch
                {
                    return Content("[]", "application/json");
                }
            }

            var list = CollectTorrents(trackerName, newtoday, updatedtoday, limit);
            return Json(list);
        }

        static List<object> CollectTorrents(string trackerName, int newtoday, int updatedtoday, int limit)
        {
            var today = DateTime.Today - (DateTime.Now - DateTime.UtcNow);
            var collected = new List<TorrentDetails>();
            var filterByTracker = !string.IsNullOrWhiteSpace(trackerName);

            try
            {
                foreach (var item in FileDB.masterDb.ToArray())
                {
                    var db = FileDB.OpenRead(item.Key, cache: false);
                    if (db == null) continue;

                    foreach (var t in db.Values)
                    {
                        if (t == null || string.IsNullOrEmpty(t.trackerName))
                            continue;

                        if (filterByTracker && !string.Equals(t.trackerName, trackerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (newtoday == 1 && t.createTime < today) continue;
                        if (updatedtoday == 1 && t.updateTime < today) continue;

                        collected.Add(t);
                    }
                }
            }
            catch { }

            return collected
                .OrderByDescending(t => t.createTime)
                .Take(limit)
                .Select(t => new
                {
                    t.trackerName,
                    t.types,
                    url = t.url,
                    t.title,
                    t.sid,
                    t.pir,
                    t.sizeName,
                    createTime = t.createTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    updateTime = t.updateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    hasMagnet = !string.IsNullOrEmpty(t.magnet),
                    t.name,
                    t.originalname,
                    t.relased
                })
                .Cast<object>()
                .ToList();
        }
    }
}
