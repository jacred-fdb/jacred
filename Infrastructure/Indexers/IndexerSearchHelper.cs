using JacRed.Models.Api;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;

namespace JacRed.Infrastructure.Indexers
{
    public static class IndexerSearchHelper
    {
        public static IndexerSearchRequest BuildRequest(
            IQueryCollection query,
            string apikey,
            bool rqnum,
            string boundQuery = null,
            string boundTitle = null,
            string boundTitleOriginal = null,
            int boundYear = 0,
            int boundIsSerial = -1)
        {
            string resolvedQuery = boundQuery;
            if (string.IsNullOrWhiteSpace(resolvedQuery))
                resolvedQuery = IndexerRequestParams.ResolveSearchQuery(query);

            string title = boundTitle ?? query["title"].ToString();
            string titleOriginal = boundTitleOriginal ?? query["title_original"].ToString();
            int year = boundYear > 0 ? boundYear : IndexerRequestParams.YearFromQuery(query);

            int isSerial = boundIsSerial;
            if (query.ContainsKey("is_serial") && int.TryParse(query["is_serial"], out int parsedSerial))
                isSerial = parsedSerial;

            var categories = IndexerRequestParams.CategoriesFromQuery(query);
            isSerial = ApplyCategoryIsSerialHint(isSerial, categories);

            bool cardMode = IndexerRequestParams.IsCardMetadataSearch(
                title,
                titleOriginal,
                query.ContainsKey("is_serial") || boundIsSerial >= 0 ? isSerial : (int?)null,
                categories,
                query["genres"]);

            var trackers = IndexerRequestParams.TrackersFromQuery(query);
            string tracker = trackers.Count > 0 ? trackers[0] : null;

            return new IndexerSearchRequest
            {
                Query = resolvedQuery,
                Title = title,
                TitleOriginal = titleOriginal,
                Year = year,
                IsSerial = isSerial,
                Genres = query["genres"],
                Categories = categories,
                Season = IndexerRequestParams.SeasonFromQuery(query),
                Episode = IndexerRequestParams.EpisodeFromQuery(query),
                Tracker = tracker,
                Trackers = trackers,
                CardMode = cardMode,
                ApiKey = apikey,
                RqNum = rqnum
            };
        }

        public static Dictionary<string, string> MergeCategoryDict(List<int> categories, Dictionary<string, string> bound)
        {
            if (categories != null && categories.Count > 0)
            {
                var dict = new Dictionary<string, string>();
                for (int i = 0; i < categories.Count; i++)
                    dict[$"Category[{i}]"] = categories[i].ToString();
                return dict;
            }
            return bound;
        }

        /// <summary>Same hint logic as legacy JackettSearchResults category block.</summary>
        public static int ApplyCategoryIsSerialHint(int isSerial, List<int> categories)
        {
            if (isSerial != 0 || categories == null || categories.Count == 0)
                return isSerial;

            string cat = string.Join(",", categories);
            if (cat.Contains("5020") || cat.Contains("2010"))
                return 3;
            if (cat.Contains("5080"))
                return 4;
            if (cat.Contains("5070"))
                return 5;
            if (cat.StartsWith("20"))
                return 1;
            if (cat.StartsWith("50"))
                return 2;
            return isSerial;
        }

        public static string CategoryParam(IQueryCollection query)
        {
            string catParam = query["cat"].ToString();
            if (!string.IsNullOrWhiteSpace(catParam))
                return catParam;

            var cats = IndexerRequestParams.CategoriesFromQuery(query);
            return cats.Count > 0 ? string.Join(",", cats) : "";
        }

        public static List<Result> ApplyPostFilters(
            List<Result> results,
            IQueryCollection query,
            IndexerSearchRequest req,
            string torznabAction = null)
        {
            var settings = IndexerSearchOptions.Resolve();
            string catParam = CategoryParam(query);

            bool applyCatFilter = !req.CardMode && !settings.skipCatFilter && !string.IsNullOrWhiteSpace(catParam);
            if (torznabAction != null)
            {
                int isSerial = IndexerRequestParams.IsSerialFromTorznabAction(torznabAction);
                if (query.ContainsKey("is_serial") && int.TryParse(query["is_serial"], out int parsedSerial))
                    isSerial = parsedSerial;
                applyCatFilter = applyCatFilter && isSerial < 0;
            }

            if (applyCatFilter)
                results = IndexerResultFilters.FilterByCategory(results, catParam);

            if (req.Year > 0 && !req.CardMode)
                results = IndexerResultFilters.FilterByYear(results, req.Year);

            if (req.Season.HasValue)
                results = SeasonEpisodeFilter.Filter(results, req.Season.Value, req.Episode);

            results = IndexerResultFilters.FilterByTrackers(results, req.Trackers);

            var (limit, offset) = IndexerRequestParams.LimitOffsetFromQuery(query);
            return IndexerResultFilters.Paginate(results, limit, offset);
        }
    }
}
