using JacRed.Engine;
using JacRed.Engine.Indexers;
using JacRed.Models.Api;
using JacRed.Models.AppConf;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JacRed.Controllers
{
    /// <summary>
    /// Native Torznab XML API (jacred-proxy compatibility without external worker).
    /// </summary>
    public class TorznabController : BaseController
    {
        [Route("/api")]
        [Route("/api/v2.0/indexers/{indexer}/results/torznab/api")]
        public async Task<IActionResult> Torznab(string indexer, string t, string apikey)
        {
            if (AppInit.conf.torznab != null && !AppInit.conf.torznab.enable)
                return NotFound();

            var query = HttpContext.Request.Query;
            var origin = $"{Request.Scheme}://{Request.Host}";

            if (Request.Method == "HEAD")
                return Content("", "application/xml; charset=utf-8");

            if (t == "caps")
                return Content(TorznabXmlFormatter.CapsXml(origin), "application/xml; charset=utf-8");

            if (t == "indexers")
            {
                var configured = (query["configured"].ToString() ?? "").ToLowerInvariant();
                if (configured == "" || configured == "true")
                    return Content(TorznabXmlFormatter.IndexersXml, "application/xml; charset=utf-8");
                return Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><indexers></indexers>", "application/xml; charset=utf-8");
            }

            string resolvedQuery = IndexerRequestParams.ResolveSearchQuery(query);
            if (IndexerRequestParams.TvdbIdOnly(query, resolvedQuery))
                return XmlSearchResult(new List<Result>(), t, query, origin);

            string title = query["title"].ToString();
            string titleOriginal = query["title_original"].ToString();
            if (string.IsNullOrWhiteSpace(resolvedQuery) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(titleOriginal))
                return XmlSearchResult(new List<Result>(), t, query, origin);

            int year = IndexerRequestParams.YearFromQuery(query);
            int isSerial = IndexerRequestParams.IsSerialFromTorznabAction(t);
            if (query.ContainsKey("is_serial") && int.TryParse(query["is_serial"], out int parsedSerial))
                isSerial = parsedSerial;

            var categories = IndexerRequestParams.CategoriesFromQuery(query);
            bool cardMode = IndexerRequestParams.IsCardMetadataSearch(
                title, titleOriginal,
                query.ContainsKey("is_serial") ? isSerial : (int?)null,
                categories, query["genres"]);

            var req = new IndexerSearchRequest
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
                Tracker = query["tracker"],
                CardMode = cardMode,
                ApiKey = apikey,
                RqNum = false
            };

            var results = await IndexerSearchEngine.SearchCombinedAsync(req, memoryCache);
            results = ApplyPostFilters(results, t, query, year, cardMode, req.Season, req.Episode);
            return XmlSearchResult(results, t, query, origin);
        }

        IActionResult XmlSearchResult(List<Result> results, string t, IQueryCollection query, string origin)
        {
            string assignedCat = "";
            string catParam = query["cat"].ToString();
            if (t == "tvsearch" || t == "tv") assignedCat = "5000";
            else if (t == "moviesearch" || t == "movie") assignedCat = "2000";
            else if (!string.IsNullOrWhiteSpace(catParam)) assignedCat = catParam.Split(',')[0].Trim();

            bool enrich = AppInit.conf.torznab?.enrichTitles ?? true;
            string items = TorznabXmlFormatter.ItemsXml(results, assignedCat, enrich, catParam);
            return Content(TorznabXmlFormatter.WrapRss(items, origin), "application/xml; charset=utf-8");
        }

        static List<Result> ApplyPostFilters(List<Result> results, string t, IQueryCollection query, int year, bool cardMode, int? season, int? episode)
        {
            var settings = AppInit.conf.torznab ?? new TorznabSettings();
            string catParam = query["cat"].ToString();
            int isSerial = IndexerRequestParams.IsSerialFromTorznabAction(t);
            if (query.ContainsKey("is_serial") && int.TryParse(query["is_serial"], out int parsedSerial))
                isSerial = parsedSerial;

            if (isSerial < 0 && !string.IsNullOrWhiteSpace(catParam) && !cardMode && !settings.skipCatFilter)
                results = IndexerResultFilters.FilterByCategory(results, catParam);

            if (year > 0 && !cardMode)
                results = IndexerResultFilters.FilterByYear(results, year);

            if (season.HasValue)
                results = SeasonEpisodeFilter.Filter(results, season.Value, episode);

            var (limit, offset) = IndexerRequestParams.LimitOffsetFromQuery(query);
            return IndexerResultFilters.Paginate(results, limit, offset);
        }
    }

    /// <summary>Jackett/Prowlarr metadata endpoints.</summary>
    public class JackettMetaController : BaseController
    {
        [Route("/api/v2.0/indexers")]
        public IActionResult IndexersList()
        {
            return Json(new[]
            {
                new
                {
                    id = "all",
                    name = "JacRed (all trackers)",
                    description = "Aggregated JacRed search across all configured trackers",
                    type = "public",
                    configured = true,
                    link = "https://github.com/jacred-fdb/jacred"
                }
            });
        }

        [Route("/api/v1/indexer")]
        public IActionResult ProwlarrStub()
        {
            return Json(new
            {
                Indexers = new[]
                {
                    new { id = "all", name = "JacRed (all trackers)", configured = true }
                }
            });
        }
    }
}
