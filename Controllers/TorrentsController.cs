using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using JacRed.Engine.CORE;
using System.Text.RegularExpressions;
using JacRed.Engine;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.Tasks;
using System;
using JacRed.Models.Details;

namespace JacRed.Controllers
{
    public class TorrentsController : BaseController
    {
        #region Torrents
        [Route("/api/v1.0/torrents")]
        async public Task<JsonResult> Torrents(string search, string altname, bool exact, string type, string sort, string tracker, string voice, string videotype, long relased, long quality, long season)
        {
            #region search kp/imdb
            if (!string.IsNullOrWhiteSpace(search) && Regex.IsMatch(search.Trim(), "^(tt|kp)[0-9]+$"))
            {
                string memkey = $"api/v1.0/torrents:{search}";
                if (!memoryCache.TryGetValue(memkey, out (string original_name, string name) cache))
                {
                    search = search.Trim();
                    string uri = $"&imdb={search}";
                    if (search.StartsWith("kp"))
                        uri = $"&kp={search.Remove(0, 2)}";

                    var root = await HttpClient.Get<JObject>("https://api.apbugall.org/?token=04941a9a3ca3ac16e2b4327347bbc1" + uri, timeoutSeconds: 8);
                    cache.original_name = root?.Value<JObject>("data")?.Value<string>("original_name");
                    cache.name = root?.Value<JObject>("data")?.Value<string>("name");

                    memoryCache.Set(memkey, cache, DateTime.Now.AddDays(1));
                }

                if (!string.IsNullOrWhiteSpace(cache.name) && !string.IsNullOrWhiteSpace(cache.original_name))
                {
                    search = cache.original_name;
                    altname = cache.name;
                }
                else
                {
                    search = cache.original_name ?? cache.name;
                }
            }
            #endregion

            #region Выборка
            var torrents = new Dictionary<string, TorrentDetails>();

            #region AddTorrents
            void AddTorrents(TorrentDetails t)
            {
                if (AppInit.conf.synctrackers != null && !AppInit.conf.synctrackers.Contains(t.trackerName))
                    return;

                if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.trackerName))
                    return;

                if (torrents.TryGetValue(t.url, out TorrentDetails val))
                {
                    if (t.updateTime > val.updateTime)
                        torrents[t.url] = t;
                }
                else
                {
                    torrents.TryAdd(t.url, t);
                }
            }
            #endregion

            if (string.IsNullOrWhiteSpace(search) || search.Length == 1)
                return Json(torrents);

            string _s = StringConvert.SearchName(search);
            string _altsearch = StringConvert.SearchName(altname);

            if (string.IsNullOrEmpty(_s) && string.IsNullOrEmpty(_altsearch))
                return Json(torrents);

            if (exact)
            {
                #region Точный поиск
                foreach (var mdb in FileDB.masterDb.Where(i => (_s != null && (i.Key.StartsWith($"{_s}:") || i.Key.EndsWith($":{_s}"))) || (_altsearch != null && i.Key.Contains(_altsearch))))
                {
                    foreach (var t in FileDB.OpenRead(mdb.Key, true).Values)
                    {
                        if (t.types == null)
                            continue;

                        if (string.IsNullOrWhiteSpace(type) || t.types.Contains(type))
                        {
                            string _n = t._sn ?? StringConvert.SearchName(t.name);
                            string _o = t._so ?? StringConvert.SearchName(t.originalname);

                            if (_n == _s || _o == _s || (_altsearch != null && (_n == _altsearch || _o == _altsearch)))
                                AddTorrents(t);
                        }
                    }

                }
                #endregion
            }
            else
            {
                #region Поиск по совпадению ключа в имени
                var mdb = FileDB.masterDb.Where(i => (_s != null && i.Key.Contains(_s)) || (_altsearch != null && i.Key.Contains(_altsearch)));
                if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
                    mdb = mdb.Take(AppInit.conf.maxreadfile);

                foreach (var val in mdb)
                {
                    foreach (var t in FileDB.OpenRead(val.Key, true).Values)
                    {
                        if (t.types == null)
                            continue;

                        if (string.IsNullOrWhiteSpace(type) || t.types.Contains(type))
                            AddTorrents(t);
                    }

                }
                #endregion
            }

            if (torrents.Count == 0)
                return Json(torrents);

            IEnumerable<TorrentDetails> query = torrents.Values;

            #region sort
            switch (sort ?? string.Empty)
            {
                case "sid":
                    query = query.OrderByDescending(i => i.sid);
                    break;
                case "pir":
                    query = query.OrderByDescending(i => i.pir);
                    break;
                case "size":
                    query = query.OrderByDescending(i => i.size);
                    break;
                case "create":
                    query = query.OrderByDescending(i => i.createTime);
                    break;
                case "update":
                    query = query.OrderByDescending(i => i.updateTime);
                    break;
            }
            #endregion

            if (!string.IsNullOrWhiteSpace(tracker))
                query = query.Where(i => i.trackerName == tracker);

            if (relased > 0)
                query = query.Where(i => i.relased == relased);

            if (quality > 0)
                query = query.Where(i => i.quality == quality);

            if (!string.IsNullOrWhiteSpace(videotype))
                query = query.Where(i => i.videotype == videotype);

            if (!string.IsNullOrWhiteSpace(voice))
                query = query.Where(i => i.voices.Contains(voice));

            if (season > 0)
                query = query.Where(i => i.seasons.Contains((int)season));
            #endregion

            return Json(query.Take(2_000).Select(i => new
            {
                tracker = i.trackerName,
                url = i.url != null && i.url.StartsWith("http") ? i.url : null,
                i.title,
                i.size,
                i.sizeName,
                i.createTime,
                i.updateTime,
                i.sid,
                i.pir,
                i.magnet,
                i.name,
                i.originalname,
                i.relased,
                i.videotype,
                i.quality,
                i.voices,
                i.seasons,
                i.types
            }));
        }
        #endregion

        #region Qualitys
        [Route("/api/v1.0/qualitys")]
        public JsonResult Qualitys(string name, string originalname, string type, int page = 1, int take = 1000)
        {
            string _s = StringConvert.SearchName(name);
            string _so = StringConvert.SearchName(originalname);

            if (string.IsNullOrEmpty(_s) && string.IsNullOrEmpty(_so))
                return Json(new Dictionary<string, Dictionary<int, Models.TorrentQuality>>());

            var torrents = new Dictionary<string, Dictionary<int, Models.TorrentQuality>>();

            #region AddTorrents
            void AddTorrents(TorrentDetails t)
            {
                if (t?.types == null || t.types.Contains("sport") || t.relased == 0)
                    return;

                if (!string.IsNullOrEmpty(type) && !t.types.Contains(type))
                    return;

                string key = $"{StringConvert.SearchName(t.name)}:{StringConvert.SearchName(t.originalname)}";

                var langs = t.languages;

                if (t.ffprobe != null || !AppInit.conf.tracks)
                    langs = TracksDB.Languages(t, t.ffprobe);
                else
                {
                    var streams = TracksDB.Get(t.magnet, t.types);
                    langs = TracksDB.Languages(t, streams ?? t.ffprobe);
                }

                var model = new Models.TorrentQuality()
                {
                    types = t.types.ToHashSet(),
                    createTime = t.createTime,
                    updateTime = t.updateTime,
                    languages = langs ?? new HashSet<string>(),
                    qualitys = new HashSet<int>() { t.quality }
                };

                if (torrents.TryGetValue(key, out Dictionary<int, Models.TorrentQuality> val))
                {
                    if (val.TryGetValue(t.relased, out Models.TorrentQuality _md))
                    {
                        if (langs != null)
                        {
                            foreach (var item in langs)
                                _md.languages.Add(item);
                        }

                        if (t.types != null)
                        {
                            foreach (var item in t.types)
                                _md.types.Add(item);
                        }

                        _md.qualitys.Add(t.quality);

                        if (_md.createTime > t.createTime)
                            _md.createTime = t.createTime;

                        if (t.updateTime > _md.updateTime)
                            _md.updateTime = t.updateTime;

                        val[t.relased] = _md;
                    }
                    else
                    {
                        val.TryAdd(t.relased, model);
                    }

                    torrents[key] = val;
                }
                else
                {
                    torrents.TryAdd(key, new Dictionary<int, Models.TorrentQuality>() { [t.relased] = model });
                }
            }
            #endregion

            IEnumerable<KeyValuePair<string, Models.TorrentInfo>> mdb = FileDB.masterDb;

            if (!string.IsNullOrEmpty(_s) && !string.IsNullOrEmpty(_so))
            {
                mdb = mdb.Where(i => i.Key.Contains(_s) || i.Key.Contains(_so));
            }
            else if (!string.IsNullOrEmpty(_s))
            {
                mdb = mdb.Where(i => i.Key.Contains(_s));
            }
            else if (!string.IsNullOrEmpty(_so))
            {
                mdb = mdb.Where(i => i.Key.Contains(_so));
            }

            mdb = mdb.OrderByDescending(i => i.Value.updateTime);

            if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
                mdb = mdb.Take(AppInit.conf.maxreadfile);

            var mdbList = mdb.ToList();

            foreach (var val in mdbList)
            {
                foreach (var t in FileDB.OpenRead(val.Key, true).Values)
                    AddTorrents(t);
            }

            if (take == -1)
                return Json(torrents);
            var orderedTorrents = torrents.OrderByDescending(kvp =>
                kvp.Value.Values.Max(v => v.updateTime)).ToList();

            int skip = (page - 1) * take;
            if (skip < 0) skip = 0;

            var paginated = orderedTorrents.Skip(skip).Take(take);
            var result = paginated.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return Json(result);
        }
        #endregion
    }
}
