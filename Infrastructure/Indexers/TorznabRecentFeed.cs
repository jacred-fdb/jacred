using JacRed.Application.Search;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Api;
using JacRed.Models.Details;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>
    /// Torznab RSS: search with no query returns the newest releases.
    /// </summary>
    public static class TorznabRecentFeed
    {
        public static List<Result> Latest(IQueryCollection query, string indexer, string t, string apikey)
        {
            var req = IndexerSearchHelper.BuildRequest(query, apikey, rqnum: false);
            TrackerNameMatching.ApplyIndexerPathFilter(req, indexer);

            string cat = EffectiveCat(t, IndexerSearchHelper.CategoryParam(query));
            var (limit, offset) = IndexerRequestParams.LimitOffsetFromQuery(query);
            int page = Math.Min(limit ?? IndexerResultFilters.DefaultLimit, IndexerResultFilters.MaxLimit);

            var torrents = ReadNewest(cat, req.Trackers, page + Math.Max(0, offset));
            var results = JackettResultBuilder.Build(torrents, apikey, rqnum: false);
            return Select(results, cat, req.Trackers, req.Year, req.Season, req.Episode, page, offset);
        }

        internal static string EffectiveCat(string t, string catParam)
        {
            if (!string.IsNullOrWhiteSpace(catParam))
                return catParam;
            if (t == "tvsearch" || t == "tv")
                return "5000";
            if (t == "moviesearch" || t == "movie")
                return "2000";
            return null;
        }

        /// <summary>Sort, category, tracker, year, season, then page. No FileDB access.</summary>
        internal static List<Result> Select(
            IEnumerable<Result> items,
            string cat,
            List<string> trackers,
            int year,
            int? season,
            int? episode,
            int? limit,
            int offset)
        {
            var list = (items ?? Enumerable.Empty<Result>()).OrderByDescending(i => i.PublishDate).ToList();
            if (!string.IsNullOrWhiteSpace(cat))
            {
                list = list.Where(i => i.Category != null && i.Category.Count > 0).ToList();
                list = IndexerResultFilters.FilterByCategory(list, cat);
            }

            list = IndexerResultFilters.FilterByTrackers(list, trackers);
            if (year > 0)
                list = IndexerResultFilters.FilterByYear(list, year);
            if (season.HasValue)
                list = SeasonEpisodeFilter.Filter(list, season.Value, episode);

            int page = Math.Min(limit ?? IndexerResultFilters.DefaultLimit, IndexerResultFilters.MaxLimit);
            return IndexerResultFilters.Paginate(list, page, offset);
        }

        static Dictionary<string, TorrentDetails> ReadNewest(string cat, List<string> trackers, int need)
        {
            var torrents = new Dictionary<string, TorrentDetails>();
            if (need < 1)
                return torrents;

            // ponytail: newest maxreadfile shards by masterDb.updateTime, not a global createTime index.
            // A rare category can miss releases outside that window. Upgrade: a small recent-torrent index.
            int cap = Math.Max(1, AppInit.conf.maxreadfile);
            int matches = 0;

            foreach (var shard in FileDB.masterDb.OrderByDescending(s => s.Value.updateTime).Take(cap))
            {
                foreach (var torrent in FileDB.OpenRead(shard.Key, update_lastread: true).Values)
                {
                    if (torrent.types == null || torrent.types.Length == 0 || string.IsNullOrEmpty(torrent.url))
                        continue;
                    if (!TrackerNameMatching.Matches(torrent.trackerName, trackers))
                        continue;
                    if (!string.IsNullOrWhiteSpace(cat) && !MatchesCat(torrent, cat))
                        continue;

                    int before = torrents.Count;
                    JackettResultBuilder.AddTorrent(torrents, torrent);
                    if (torrents.Count > before && ++matches >= need)
                        return torrents;
                }
            }

            return torrents;
        }

        static bool MatchesCat(TorrentDetails torrent, string cat)
        {
            var ids = JackettResultBuilder.CategoryIds(torrent);
            if (ids.Count == 0)
                return false;

            var one = new List<Result> { new Result { Category = ids } };
            return IndexerResultFilters.FilterByCategory(one, cat).Count == 1;
        }
    }
}
