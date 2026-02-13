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
using System.Web;
using MonoTorrent;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using JacRed.Models.Api;
using Microsoft.AspNetCore.Http;

namespace JacRed.Controllers
{
    public class ApiController : BaseController
    {
        [Route("/")]
        public ActionResult Index()
        {
            return File(System.IO.File.OpenRead("wwwroot/index.html"), "text/html");
        }

        [Route("health")]
        public IActionResult Health()
        {
            return Json(new Dictionary<string, string>
            {
                ["status"] = "OK"
            });
        }

        [Route("version")]
        public IActionResult Version()
        {
            return Json(new Dictionary<string, string>
            {
                ["version"] = "2.0.0"
            });
        }

        [Route("lastupdatedb")]
        public IActionResult LastUpdateDB()
        {
            string lastUpdate = "01.01.2000 01:01";
            if (FileDB.masterDb != null && FileDB.masterDb.Count > 0)
                lastUpdate = FileDB.masterDb.OrderByDescending(i => i.Value.updateTime).First().Value.updateTime.ToString("dd.MM.yyyy HH:mm");

            return Json(new Dictionary<string, string>
            {
                ["lastupdatedb"] = lastUpdate
            });
        }

        [Route("api/v1.0/conf")]
        public JsonResult JacRedConf(string apikey)
        {
            return Json(new
            {
                apikey = string.IsNullOrWhiteSpace(AppInit.conf.apikey) || apikey == AppInit.conf.apikey
            });
        }

        #region Jackett
        [Route("/api/v2.0/indexers/{status}/results")]
        public ActionResult Jackett(string apikey, string query, string title, string title_original, int year, Dictionary<string, string> category, int is_serial = -1)
        {
            //Console.WriteLine(HttpContext.Request.Path + HttpContext.Request.QueryString.Value);

            string cachekey = $"api:v2.0:indexers:{query}:{title}:{title_original}:{year}:{(category != null && category.Count > 0 ? string.Join(",", category.Select(i => $"{i.Key}={i.Value}")) : "null")}:{is_serial}";
            if (memoryCache.TryGetValue(cachekey, out List<Result> _cacheResult))
                return Json(new RootObject() { Results = _cacheResult });

            var fastdb = getFastdb();
            var torrents = new Dictionary<string, TorrentDetails>();
            bool rqnum = !HttpContext.Request.QueryString.Value.Contains("&is_serial=") && HttpContext.Request.Headers.UserAgent.ToString() == "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36";

            #region Запрос с NUM
            if (rqnum && query != null)
            {
                var mNum = Regex.Match(query, "^([^a-z-A-Z]+) ([^а-я-А-Я]+) ([0-9]{4})$");

                if (mNum.Success)
                {
                    if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z0-9]{2}"))
                    {
                        var g = mNum.Groups;
                        title = g[1].Value;
                        title_original = g[2].Value;
                        year = int.Parse(g[3].Value);
                    }
                }
                else
                {
                    if (Regex.IsMatch(query, "^([^a-z-A-Z]+) ((19|20)[0-9]{2})$"))
                        return Json(new RootObject() { Results = new List<Result>() });

                    mNum = Regex.Match(query, "^([^a-z-A-Z]+) ([^а-я-А-Я]+)$");

                    if (mNum.Success)
                    {
                        if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z0-9]{2}"))
                        {
                            var g = mNum.Groups;
                            title = g[1].Value;
                            title_original = g[2].Value;
                        }
                    }
                }
            }
            #endregion

            #region category
            if (is_serial == 0 && category != null)
            {
                string cat = category.FirstOrDefault().Value;
                if (cat != null)
                {
                    if (cat.Contains("5020") || cat.Contains("2010"))
                        is_serial = 3; // tvshow
                    else if (cat.Contains("5080"))
                        is_serial = 4; // док
                    else if (cat.Contains("5070"))
                        is_serial = 5; // аниме
                    else if (is_serial == 0)
                    {
                        if (cat.StartsWith("20"))
                            is_serial = 1; // фильм
                        else if (cat.StartsWith("50"))
                            is_serial = 2; // сериал
                    }
                }
            }
            #endregion

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

            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(title_original))
            {
                #region Точный поиск
                string _n = StringConvert.SearchName(title);
                string _o = StringConvert.SearchName(title_original);

                HashSet<string> keys = new HashSet<string>(20);

                void updateKeys(string k)
                {
                    if (k != null && fastdb.TryGetValue(k, out List<string> _keys))
                    {
                        foreach (string val in _keys)
                            keys.Add(val);
                    }
                }

                updateKeys(_n);
                updateKeys(_o);

                if ((!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0) && keys.Count > AppInit.conf.maxreadfile)
                    keys = keys.Take(AppInit.conf.maxreadfile).ToHashSet();

                foreach (string key in keys)
                {
                    foreach (var t in FileDB.OpenRead(key, true).Values)
                    {
                        if (t.types == null || t.title.Contains(" КПК"))
                            continue;

                        string name = t._sn ?? StringConvert.SearchName(t.name);
                        string originalname = t._so ?? StringConvert.SearchName(t.originalname);

                        // Точная выборка по name или originalname
                        if ((_n != null && _n == name) || (_o != null && _o == originalname))
                        {
                            if (is_serial == 1)
                            {
                                #region Фильм
                                if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("anime") || t.types.Contains("documovie"))
                                {
                                    if (Regex.IsMatch(t.title, " (сезон|сери(и|я|й))", RegexOptions.IgnoreCase))
                                        continue;

                                    if (year > 0)
                                    {
                                        if (t.relased == year || t.relased == (year - 1) || t.relased == (year + 1))
                                            AddTorrents(t);
                                    }
                                    else
                                    {
                                        AddTorrents(t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 2)
                            {
                                #region Сериал
                                if (t.types.Contains("serial") || t.types.Contains("multserial") || t.types.Contains("anime") || t.types.Contains("docuserial") || t.types.Contains("tvshow"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            AddTorrents(t);
                                    }
                                    else
                                    {
                                        AddTorrents(t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 3)
                            {
                                #region tvshow
                                if (t.types.Contains("tvshow"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            AddTorrents(t);
                                    }
                                    else
                                    {
                                        AddTorrents(t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 4)
                            {
                                #region docuserial / documovie
                                if (t.types.Contains("docuserial") || t.types.Contains("documovie"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            AddTorrents(t);
                                    }
                                    else
                                    {
                                        AddTorrents(t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 5)
                            {
                                #region anime
                                if (t.types.Contains("anime"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            AddTorrents(t);
                                    }
                                    else
                                    {
                                        AddTorrents(t);
                                    }
                                }
                                #endregion
                            }
                            else
                            {
                                #region Неизвестно
                                if (year > 0)
                                {
                                    if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("documovie"))
                                    {
                                        if (t.relased == year || t.relased == (year - 1) || t.relased == (year + 1))
                                            AddTorrents(t);
                                    }
                                    else
                                    {
                                        if (t.relased >= (year - 1))
                                            AddTorrents(t);
                                    }
                                }
                                else
                                {
                                    AddTorrents(t);
                                }
                                #endregion
                            }
                        }
                    }

                }
                #endregion
            }
            else if (!string.IsNullOrWhiteSpace(query) && query.Length > 1)
            {
                #region Обычный поиск
                string _s = StringConvert.SearchName(query);

                #region torrentsSearch
                void torrentsSearch(bool exact, bool exactdb)
                {
                    if (_s == null)
                        return;

                    HashSet<string> keys = null;

                    if (exactdb)
                    {
                        if (fastdb.TryGetValue(_s, out List<string> _keys) && _keys.Count > 0)
                        {
                            keys = new HashSet<string>(_keys.Count);

                            foreach (string val in _keys)
                                keys.Add(val);
                        }
                    }
                    else
                    {
                        string mkey = $"api:torrentsSearch:{_s}";
                        if (!memoryCache.TryGetValue(mkey, out keys))
                        {
                            keys = new HashSet<string>();

                            foreach (var f in fastdb.Where(i => i.Key.Contains(_s)))
                            {
                                foreach (string k in f.Value)
                                    keys.Add(k);

                                if ((!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0) && keys.Count > AppInit.conf.maxreadfile)
                                    break;
                            }

                            memoryCache.Set(mkey, keys, DateTime.Now.AddHours(1));
                        }
                    }

                    if (keys != null && keys.Count > 0)
                    {
                        foreach (string key in keys)
                        {
                            foreach (var t in FileDB.OpenRead(key, true).Values)
                            {
                                if (exact)
                                {
                                    if ((t._sn ?? StringConvert.SearchName(t.name)) != _s && (t._so ?? StringConvert.SearchName(t.originalname)) != _s)
                                        continue;
                                }

                                if (t.types == null || t.title.Contains(" КПК"))
                                    continue;

                                if (is_serial == 1)
                                {
                                    if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("anime") || t.types.Contains("documovie"))
                                        AddTorrents(t);
                                }
                                else if (is_serial == 2)
                                {
                                    if (t.types.Contains("serial") || t.types.Contains("multserial") || t.types.Contains("anime") || t.types.Contains("docuserial") || t.types.Contains("tvshow"))
                                        AddTorrents(t);
                                }
                                else if (is_serial == 3)
                                {
                                    if (t.types.Contains("tvshow"))
                                        AddTorrents(t);
                                }
                                else if (is_serial == 4)
                                {
                                    if (t.types.Contains("docuserial") || t.types.Contains("documovie"))
                                        AddTorrents(t);
                                }
                                else if (is_serial == 5)
                                {
                                    if (t.types.Contains("anime"))
                                        AddTorrents(t);
                                }
                                else
                                {
                                    AddTorrents(t);
                                }
                            }

                        }
                    }
                }
                #endregion

                if (is_serial == -1)
                {
                    torrentsSearch(exact: false, exactdb: true);
                    if (torrents.Count == 0)
                        torrentsSearch(exact: false, exactdb: false);
                }
                else
                {
                    torrentsSearch(exact: true, exactdb: true);
                    if (torrents.Count == 0)
                        torrentsSearch(exact: false, exactdb: false);
                }
                #endregion
            }

            #region getCategoryIds
            HashSet<int> getCategoryIds(TorrentDetails t, out string categoryDesc)
            {
                categoryDesc = null;
                HashSet<int> categoryIds = new HashSet<int>(t.types.Length);

                foreach (string type in t.types)
                {
                    switch (type)
                    {
                        case "movie":
                            categoryDesc = "Movies";
                            categoryIds.Add(2000);
                            break;

                        case "serial":
                            categoryDesc = "TV";
                            categoryIds.Add(5000);
                            break;

                        case "documovie":
                        case "docuserial":
                            categoryDesc = "TV/Documentary";
                            categoryIds.Add(5080);
                            break;

                        case "tvshow":
                            categoryDesc = "TV/Foreign";
                            categoryIds.Add(5020);
                            categoryIds.Add(2010);
                            break;

                        case "anime":
                            categoryDesc = "TV/Anime";
                            categoryIds.Add(5070);
                            break;
                    }
                }

                return categoryIds;
            }
            #endregion

            #region Объединить дубликаты
            IEnumerable<TorrentDetails> result = null;

            if ((!rqnum && AppInit.conf.mergeduplicates) || (rqnum && AppInit.conf.mergenumduplicates))
            {
                Dictionary<string, (TorrentDetails torrent, string title, string Name, List<string> AnnounceUrls)> temp = new Dictionary<string, (TorrentDetails, string, string, List<string>)>();

                foreach (var torrent in torrents.Values.OrderByDescending(i => i.createTime).ThenBy(i => i.trackerName == "selezen"))
                {
                    var magnetLink = MagnetLink.Parse(torrent.magnet);
                    string hex = magnetLink.InfoHashes.V1OrV2.ToHex();

                    if (!temp.TryGetValue(hex, out _))
                    {
                        temp.TryAdd(hex, ((TorrentDetails)torrent.Clone(), torrent.trackerName == "kinozal" ? torrent.title : null, magnetLink.Name, magnetLink.AnnounceUrls?.ToList() ?? new List<string>()));
                    }
                    else
                    {
                        var t = temp[hex];

                        if (!t.torrent.trackerName.Contains(torrent.trackerName))
                            t.torrent.trackerName += $", {torrent.trackerName}";

                        #region UpdateMagnet
                        void UpdateMagnet()
                        {
                            string magnet = $"magnet:?xt=urn:btih:{hex.ToLower()}";

                            if (!string.IsNullOrWhiteSpace(t.Name))
                                magnet += $"&dn={HttpUtility.UrlEncode(t.Name)}";

                            if (t.AnnounceUrls.Count > 0)
                            {
                                foreach (string announce in t.AnnounceUrls)
                                {
                                    string tr = announce.Contains("/") || announce.Contains(":") ? HttpUtility.UrlEncode(announce) : announce;

                                    if (!magnet.Contains(tr))
                                        magnet += $"&tr={tr}";
                                }
                            }

                            t.torrent.magnet = magnet;
                        }
                        #endregion

                        if (string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(magnetLink.Name))
                        {
                            t.Name = magnetLink.Name;
                            temp[hex] = t;
                            UpdateMagnet();
                        }

                        if (magnetLink.AnnounceUrls != null && magnetLink.AnnounceUrls.Count > 0)
                        {
                            t.AnnounceUrls.AddRange(magnetLink.AnnounceUrls);
                            UpdateMagnet();
                        }

                        #region UpdateTitle
                        void UpdateTitle()
                        {
                            if (string.IsNullOrWhiteSpace(t.title))
                                return;

                            string title = t.title;

                            if (t.torrent.voices != null && t.torrent.voices.Count > 0)
                                title += $" | {string.Join(" | ", t.torrent.voices)}";

                            t.torrent.title = title;
                        }

                        if (torrent.trackerName == "kinozal")
                        {
                            t.title = torrent.title;
                            temp[hex] = t;
                            UpdateTitle();
                        }

                        if (torrent.voices != null && torrent.voices.Count > 0)
                        {
                            if (t.torrent.voices == null)
                            {
                                t.torrent.voices = torrent.voices;
                            }
                            else
                            {
                                foreach (var v in torrent.voices)
                                    t.torrent.voices.Add(v);
                            }

                            UpdateTitle();
                        }
                        #endregion

                        if (torrent.trackerName != "selezen")
                        {
                            if (torrent.sid > t.torrent.sid)
                                t.torrent.sid = torrent.sid;

                            if (torrent.pir > t.torrent.pir)
                                t.torrent.pir = torrent.pir;
                        }

                        if (torrent.createTime > t.torrent.createTime)
                            t.torrent.createTime = torrent.createTime;

                        if (torrent.voices != null && torrent.voices.Count > 0)
                        {
                            if (t.torrent.voices == null)
                                t.torrent.voices = new HashSet<string>();

                            foreach (var v in torrent.voices)
                                t.torrent.voices.Add(v);
                        }

                        if (torrent.languages != null && torrent.languages.Count > 0)
                        {
                            if (t.torrent.languages == null)
                                t.torrent.languages = new HashSet<string>();

                            foreach (var v in torrent.languages)
                                t.torrent.languages.Add(v);
                        }

                        if (t.torrent.ffprobe == null && torrent.ffprobe != null)
                            t.torrent.ffprobe = torrent.ffprobe;
                    }
                }

                result = temp.Select(i => i.Value.torrent);
            }
            else
            {
                result = torrents.Values;
            }
            #endregion

            if (apikey == "rus")
                result = result.Where(i => (i.languages != null && i.languages.Contains("rus")) || (i.types != null && (i.types.Contains("sport") || i.types.Contains("tvshow") || i.types.Contains("docuserial"))));

            #region FFprobe
            List<ffStream> FFprobe(TorrentDetails t, out HashSet<string> langs)
            {
                langs = t.languages;

                if (t.ffprobe != null || !AppInit.conf.tracks)
                {
                    langs = TracksDB.Languages(t, t.ffprobe);
                    return t.ffprobe;
                }

                var streams = TracksDB.Get(t.magnet, t.types, onlydb: true);
                langs = TracksDB.Languages(t, streams ?? t.ffprobe);
                if (streams == null)
                    return null;

                return streams;
            }
            #endregion

            var Results = new List<Result>(torrents.Values.Count);

            foreach (var i in result)
            {
                HashSet<string> languages = null;
                var ffprobe = rqnum ? null : FFprobe(i, out languages);

                Results.Add(new Result()
                {
                    Tracker = i.trackerName,
                    Details = i.url != null && i.url.StartsWith("http") ? i.url : null,
                    Title = i.title,
                    Size = i.size,
                    PublishDate = i.createTime,
                    Category = getCategoryIds(i, out string categoryDesc),
                    CategoryDesc = categoryDesc,
                    Seeders = i.sid,
                    Peers = i.pir,
                    MagnetUri = i.magnet,
                    ffprobe = ffprobe,
                    languages = languages,
                    info = rqnum ? null : new TorrentInfo()
                    {
                        name = i.name,
                        originalname = i.originalname,
                        sizeName = i.sizeName,
                        relased = i.relased,
                        videotype = i.videotype,
                        quality = i.quality,
                        voices = i.voices,
                        seasons = i.seasons != null && i.seasons.Count > 0 ? i.seasons : null,
                        types = i.types
                    }
                });
            }

            if (AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour == 0)
                memoryCache.Set(cachekey, Results, DateTime.Now.AddMinutes(5));

            return Json(new RootObject() { Results = Results });
        }
        #endregion

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

            if (exact)
            {
                #region Точный поиск
                foreach (var mdb in FileDB.masterDb.Where(i => i.Key.StartsWith($"{_s}:") || i.Key.EndsWith($":{_s}") || (_altsearch != null && i.Key.Contains(_altsearch))))
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
                var mdb = FileDB.masterDb.Where(i => i.Key.Contains(_s) || (_altsearch != null && i.Key.Contains(_altsearch)));
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
                    var streams = TracksDB.Get(t.magnet, t.types, onlydb: true);
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

            string _s = StringConvert.SearchName(name);
            string _so = StringConvert.SearchName(originalname);

            var mdb = FileDB.masterDb.OrderByDescending(i => i.Value.updateTime).Where(i => (_s == null && _so == null) || (_s != null && i.Key.Contains(_s)) || (_so != null && i.Key.Contains(_so)));
            if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
                mdb = mdb.Take(AppInit.conf.maxreadfile);

            foreach (var val in mdb)
            {
                foreach (var t in FileDB.OpenRead(val.Key, true).Values)
                    AddTorrents(t);
            }

            if (take == -1)
                return Json(torrents);

            return Json(torrents.Skip((page * take) - take).Take(take));
        }
        #endregion


        #region getFastdb
        static Dictionary<string, List<string>> _fastdb = null;

        public static Dictionary<string, List<string>> getFastdb(bool update = false)
        {
            if (_fastdb == null || update)
            {
                if (update)
                    Console.WriteLine($"fastdb: rebuild start / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                var fastdb = new Dictionary<string, List<string>>();

                foreach (var item in FileDB.masterDb.ToArray())
                {
                    foreach (string k in item.Key.Split(":"))
                    {
                        if (string.IsNullOrEmpty(k))
                            continue;

                        if (fastdb.TryGetValue(k, out List<string> keys))
                        {
                            keys.Add(item.Key);
                        }
                        else
                        {
                            fastdb.Add(k, new List<string>() { item.Key });
                        }
                    }
                }

                _fastdb = fastdb;
                if (update)
                    Console.WriteLine($"fastdb: rebuild end / {DateTime.Now:yyyy-MM-dd HH:mm:ss} keys={fastdb.Count}");
            }

            return _fastdb;
        }
        #endregion
    }
}
