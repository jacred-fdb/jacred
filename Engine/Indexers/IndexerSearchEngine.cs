using JacRed.Engine;
using JacRed.Controllers;
using JacRed.Engine.CORE;
using JacRed.Models.Api;
using JacRed.Models.AppConf;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JacRed.Engine.Indexers
{
    public static class IndexerSearchEngine
    {
        public static async Task<List<Result>> SearchCombinedAsync(IndexerSearchRequest req, IMemoryCache cache)
        {
            var settings = AppInit.conf.torznab ?? new TorznabSettings();
            string query = IndexerRequestParams.NormalizeQuery(req.Query);

            string titleRu = req.Title;
            string titleEn = req.TitleOriginal;
            if (string.IsNullOrWhiteSpace(titleRu) && string.IsNullOrWhiteSpace(titleEn))
            {
                var split = IndexerRequestParams.SplitBilingualQuery(query);
                titleRu = split.ru;
                titleEn = split.en;
            }

            bool imdbMode = !req.CardMode && IndexerRequestParams.IsImdbOrKpQuery(query);
            var batches = new List<IEnumerable<Result>>();

            if (imdbMode)
            {
                batches.Add(await V1SearchAsync(query, null, exact: true, settings.v1Sort, req.Tracker, req.Season, cache));
                return IndexerResultMerger.MergeAndSort(batches.ToArray());
            }

            var category = BuildCategoryDict(req.Categories);
            int isSerial = ResolveIsSerial(req);

            if (req.CardMode)
            {
                var card = ApiController.JackettSearchResults(req.ApiKey, query, titleRu, titleEn, req.Year, category, isSerial, req.RqNum, cache);
                batches.Add(card);
                if (card.Count == 0)
                {
                    foreach (var variant in BuildQueryVariants(query, titleRu, titleEn, settings))
                        batches.Add(ApiController.JackettSearchResults(req.ApiKey, variant, null, null, 0, null, isSerial, false, cache));
                }
            }
            else
            {
                foreach (var variant in BuildQueryVariants(query, titleRu, titleEn, settings))
                    batches.Add(ApiController.JackettSearchResults(req.ApiKey, variant, null, null, 0, null, isSerial, false, cache));
            }

            foreach (var pair in V1Pairs(query, titleRu, titleEn, settings, req.CardMode))
                batches.Add(await V1SearchAsync(pair.search, pair.altname, exact: false, settings.v1Sort, req.Tracker, req.Season, cache));

            return IndexerResultMerger.MergeAndSort(batches.ToArray());
        }

        static Dictionary<string, string> BuildCategoryDict(List<int> categories)
        {
            if (categories == null || categories.Count == 0) return null;
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < categories.Count; i++)
                dict[$"Category[{i}]"] = categories[i].ToString();
            return dict;
        }

        static int ResolveIsSerial(IndexerSearchRequest req)
        {
            if (req.IsSerial >= 0) return req.IsSerial;
            return req.IsSerial;
        }

        static List<string> BuildQueryVariants(string query, string titleRu, string titleEn, TorznabSettings settings)
        {
            var variants = new List<string>();
            bool skipCombined = !string.IsNullOrWhiteSpace(query) && query.Contains(" / ") && (!string.IsNullOrWhiteSpace(titleRu) || !string.IsNullOrWhiteSpace(titleEn));

            if (!string.IsNullOrWhiteSpace(query) && !skipCombined)
            {
                if (settings.stripTrailingYear)
                {
                    var stripped = IndexerRequestParams.StripTrailingYear(query);
                    if (!string.IsNullOrWhiteSpace(stripped)) variants.Add(stripped);
                }
                if (!variants.Contains(query)) variants.Add(query);
            }

            foreach (var term in new[] { titleRu, titleEn })
            {
                if (!string.IsNullOrWhiteSpace(term) && !variants.Contains(term))
                    variants.Add(term);
            }

            if (variants.Count == 0 && !string.IsNullOrWhiteSpace(query))
                variants.Add(query);

            return variants;
        }

        static List<(string search, string altname)> V1Pairs(string query, string titleRu, string titleEn, TorznabSettings settings, bool cardMode)
        {
            var mode = (settings.mergeV1 ?? "auto").ToLowerInvariant();
            if (mode == "false" || mode == "0") return new List<(string, string)>();

            if (mode == "true" || mode == "1")
                return V1SearchPairs(query, titleRu, titleEn, settings, null);

            if (cardMode)
                return V1SearchPairs(query, titleRu, titleEn, settings, 2);

            return V1SearchPairs(query, titleRu, titleEn, settings, Math.Max(1, settings.maxV1Pairs));
        }

        static List<(string search, string altname)> V1SearchPairs(string query, string titleRu, string titleEn, TorznabSettings settings, int? maxPairs)
        {
            var pairs = new List<(string, string)>();
            var seen = new HashSet<string>();

            void add(string search, string altname = null)
            {
                if (string.IsNullOrWhiteSpace(search)) return;
                string key = search + "\0" + (altname ?? "");
                if (!seen.Add(key)) return;
                pairs.Add((search, altname));
            }

            if (!string.IsNullOrWhiteSpace(titleRu) && !string.IsNullOrWhiteSpace(titleEn))
            {
                add(titleEn, titleRu);
                add(titleRu, titleEn);
            }
            else if (!string.IsNullOrWhiteSpace(titleRu))
                add(titleRu, titleEn);
            else if (!string.IsNullOrWhiteSpace(titleEn))
                add(titleEn, titleRu);

            foreach (var term in BuildQueryVariants(query, titleRu, titleEn, settings))
            {
                add(term);
                if (!string.IsNullOrWhiteSpace(titleRu) && !term.Contains(titleRu)) add(term, titleRu);
                if (!string.IsNullOrWhiteSpace(titleEn) && !term.Contains(titleEn)) add(term, titleEn);
            }

            if (maxPairs.HasValue && maxPairs.Value > 0 && pairs.Count > maxPairs.Value)
                return pairs.Take(maxPairs.Value).ToList();
            return pairs;
        }

        static async Task<List<Result>> V1SearchAsync(string search, string altname, bool exact, string sort, string tracker, int? season, IMemoryCache cache)
        {
            if (string.IsNullOrWhiteSpace(search)) return new List<Result>();

            (search, altname) = await ResolveImdbSearchAsync(search, altname, cache);

            var torrents = new Dictionary<string, TorrentDetails>();
            void add(TorrentDetails t)
            {
                if (AppInit.conf.synctrackers != null && !AppInit.conf.synctrackers.Contains(t.trackerName)) return;
                if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.trackerName)) return;
                if (!torrents.TryGetValue(t.url, out var val) || t.updateTime > val.updateTime)
                    torrents[t.url] = t;
            }

            string sn = StringConvert.SearchName(search);
            string altSn = StringConvert.SearchName(altname);

            if (exact)
            {
                foreach (var mdb in FileDB.masterDb.Where(i => i.Key.StartsWith($"{sn}:") || i.Key.EndsWith($":{sn}") || (altSn != null && i.Key.Contains(altSn))))
                {
                    foreach (var t in FileDB.OpenRead(mdb.Key, true).Values)
                    {
                        if (t.types == null) continue;
                        string n = t._sn ?? StringConvert.SearchName(t.name);
                        string o = t._so ?? StringConvert.SearchName(t.originalname);
                        if (n == sn || o == sn || (altSn != null && (n == altSn || o == altSn)))
                            add(t);
                    }
                }
            }
            else
            {
                var mdb = FileDB.masterDb.Where(i => i.Key.Contains(sn) || (altSn != null && i.Key.Contains(altSn)));
                if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
                    mdb = mdb.Take(AppInit.conf.maxreadfile);
                foreach (var val in mdb)
                {
                    foreach (var t in FileDB.OpenRead(val.Key, true).Values)
                    {
                        if (t.types != null) add(t);
                    }
                }
            }

            IEnumerable<TorrentDetails> query = torrents.Values;
            switch (sort ?? "sid")
            {
                case "pir": query = query.OrderByDescending(i => i.pir); break;
                case "size": query = query.OrderByDescending(i => i.size); break;
                default: query = query.OrderByDescending(i => i.sid); break;
            }

            if (!string.IsNullOrWhiteSpace(tracker))
                query = query.Where(i => i.trackerName == tracker);
            if (season.HasValue && season.Value > 0)
                query = query.Where(i => i.seasons != null && i.seasons.Contains(season.Value));

            return query.Take(2000).Select(MapV1).ToList();
        }

        static async Task<(string search, string altname)> ResolveImdbSearchAsync(string search, string altname, IMemoryCache cache)
        {
            if (string.IsNullOrWhiteSpace(search) || !Regex.IsMatch(search.Trim(), "^(tt|kp)[0-9]+$", RegexOptions.IgnoreCase))
                return (search, altname);

            string memkey = $"api:v1.0/torrents:{search}";
            if (cache == null || !cache.TryGetValue(memkey, out (string original_name, string name) c))
            {
                search = search.Trim();
                string uri = search.StartsWith("kp", StringComparison.OrdinalIgnoreCase)
                    ? $"&kp={search.Substring(2)}"
                    : $"&imdb={search}";
                var root = await HttpClient.Get<JObject>("https://api.apbugall.org/?token=04941a9a3ca3ac16e2b4327347bbc1" + uri, timeoutSeconds: 8);
                c.original_name = root?.Value<JObject>("data")?.Value<string>("original_name");
                c.name = root?.Value<JObject>("data")?.Value<string>("name");
                cache?.Set(memkey, c, DateTime.Now.AddDays(1));
            }

            if (!string.IsNullOrWhiteSpace(c.name) && !string.IsNullOrWhiteSpace(c.original_name))
                return (c.original_name, c.name);

            return (c.original_name ?? c.name ?? search, altname);
        }

        static Result MapV1(TorrentDetails i)
        {
            var cats = new HashSet<int>();
            string catDesc = null;
            if (i.types != null)
            {
                foreach (var type in i.types)
                {
                    switch (type)
                    {
                        case "movie": cats.Add(2000); catDesc = "Movies"; break;
                        case "serial": cats.Add(5000); catDesc = "TV"; break;
                        case "anime": cats.Add(5070); catDesc = "TV/Anime"; break;
                    }
                }
            }

            return new Result
            {
                Tracker = i.trackerName,
                Details = i.url != null && i.url.StartsWith("http") ? i.url : null,
                Title = i.title,
                Size = i.size,
                PublishDate = i.createTime,
                Category = cats,
                CategoryDesc = catDesc,
                Seeders = i.sid,
                Peers = i.pir,
                MagnetUri = i.magnet,
                info = new TorrentInfo
                {
                    name = i.name,
                    originalname = i.originalname,
                    relased = i.relased,
                    voices = i.voices,
                    seasons = i.seasons,
                    types = i.types
                }
            };
        }
    }
}
