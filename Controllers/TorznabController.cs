using JacRed.Application.Search;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JacRed.Controllers
{
    /// <summary>
    /// Native Torznab XML API.
    /// </summary>
    public class TorznabController : BaseController
    {
        readonly IJackettSearchService _searchService;
        readonly ITrackerCatalogService _trackerCatalogService;

        public TorznabController(IMemoryCache memoryCache, IJackettSearchService searchService, ITrackerCatalogService trackerCatalogService) : base(memoryCache)
        {
            _searchService = searchService;
            _trackerCatalogService = trackerCatalogService;
        }

        public static bool IsTorznabXmlEnabled() =>
            AppInit.conf.torznab == null || AppInit.conf.torznab.enable;

        [Route("/torznab/api")]
        [Route("/api/v2.0/indexers/{indexer}/results/torznab/api")]
        [Route("/api/v1/indexer/{indexer}/newznab")]
        public async Task<IActionResult> Torznab(string indexer, string t, string apikey)
        {
            if (!IsTorznabXmlEnabled())
                return NotFound();

            var query = HttpContext.Request.Query;
            var origin = $"{Request.Scheme}://{Request.Host}";
            var torznabApiUrl = TorznabApiUrl(Request, origin);

            if (Request.Method == "HEAD")
                return Content("", "application/xml; charset=utf-8");

            if (t == "caps")
                return Content(TorznabXmlFormatter.CapsXml(torznabApiUrl), "application/xml; charset=utf-8");

            if (t == "indexers")
            {
                var configured = (query["configured"].ToString() ?? "").ToLowerInvariant();
                if (configured == "" || configured == "true")
                    return Content(TorznabXmlFormatter.IndexersXml(await _trackerCatalogService.GetTrackerNamesAsync()), "application/xml; charset=utf-8");
                return Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><indexers></indexers>", "application/xml; charset=utf-8");
            }

            string resolvedQuery = IndexerRequestParams.ResolveSearchQuery(query);
            if (IndexerRequestParams.TvdbIdOnly(query, resolvedQuery))
                return XmlSearchResult(new List<Result>(), t, query, origin, torznabApiUrl);

            string title = query["title"].ToString();
            string titleOriginal = query["title_original"].ToString();
            if (string.IsNullOrWhiteSpace(resolvedQuery) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(titleOriginal))
                return XmlSearchResult(new List<Result>(), t, query, origin, torznabApiUrl);

            var req = IndexerSearchHelper.BuildRequest(query, apikey, rqnum: false, boundQuery: resolvedQuery);
            var results = await IndexerSearchEngine.SearchCombinedAsync(req, memoryCache, _searchService);
            results = IndexerSearchHelper.ApplyPostFilters(results, query, req, t);
            return XmlSearchResult(results, t, query, origin, torznabApiUrl);
        }

        static string TorznabApiUrl(HttpRequest request, string origin)
        {
            var path = (request.PathBase + request.Path).Value?.TrimEnd('/');
            if (string.IsNullOrEmpty(path))
                path = "/torznab/api";
            return origin.TrimEnd('/') + path;
        }

        IActionResult XmlSearchResult(List<Result> results, string t, IQueryCollection query, string origin, string torznabApiUrl)
        {
            string assignedCat = "";
            string catParam = IndexerSearchHelper.CategoryParam(query);
            if (t == "tvsearch" || t == "tv") assignedCat = "5000";
            else if (t == "moviesearch" || t == "movie") assignedCat = "2000";
            else if (!string.IsNullOrWhiteSpace(catParam)) assignedCat = catParam.Split(',')[0].Trim();

            bool enrich = AppInit.conf.torznab?.enrichTitles ?? true;
            string items = TorznabXmlFormatter.ItemsXml(results, assignedCat, enrich, catParam);
            return Content(TorznabXmlFormatter.WrapRss(items, origin, torznabApiUrl), "application/xml; charset=utf-8");
        }
    }

    /// <summary>Jackett/Prowlarr metadata and Search Feed endpoints.</summary>
    public class JackettMetaController : BaseController
    {
        readonly IJackettSearchService _searchService;

        public JackettMetaController(IMemoryCache memoryCache, IJackettSearchService searchService) : base(memoryCache)
        {
            _searchService = searchService;
        }

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

        /// <summary>
        /// Prowlarr REST API: list indexers (used by qui/autobrr discover fallback and Prowlarr clients).
        /// Returns a JSON array matching Prowlarr's <c>/api/v1/indexer</c> schema.
        /// </summary>
        [Route("/api/v1/indexer")]
        public IActionResult ProwlarrIndexerList()
        {
            if (!TorznabController.IsTorznabXmlEnabled())
                return NotFound();

            return Json(new[]
            {
                new
                {
                    id = 1,
                    name = "JacRed (all trackers)",
                    description = "Aggregated JacRed search across all configured trackers",
                    implementation = "Torznab",
                    implementationName = "Torznab",
                    enable = true,
                    protocol = "torrent"
                }
            });
        }

        /// <summary>
        /// Prowlarr REST API: indexer detail (qui tracker domain resolution when backend=prowlarr).
        /// </summary>
        [Route("/api/v1/indexer/{id:int}")]
        public IActionResult ProwlarrIndexerDetail(int id)
        {
            if (!TorznabController.IsTorznabXmlEnabled())
                return NotFound();

            if (id != 1)
                return NotFound();

            return Json(new
            {
                id = 1,
                name = "JacRed (all trackers)",
                description = "Aggregated JacRed search across all configured trackers",
                implementation = "Torznab",
                implementationName = "Torznab",
                enable = true,
                fields = Array.Empty<object>()
            });
        }

        /// <summary>
        /// Prowlarr Search Feed: <c>GET /api/v1/search</c>
        /// (<see href="https://wiki.servarr.com/en/prowlarr/search#search-feed">Servarr wiki</see>).
        /// </summary>
        [HttpGet("/api/v1/search")]
        public async Task<IActionResult> ProwlarrSearch(string apikey)
        {
            if (!TorznabController.IsTorznabXmlEnabled())
                return NotFound();

            var query = HttpContext.Request.Query;
            if (!IndexerRequestParams.ProwlarrIndexerIdsIncludeJacRed(query))
                return Json(Array.Empty<object>());

            string type = (query["type"].ToString() ?? "search").Trim();
            if (string.IsNullOrWhiteSpace(type))
                type = "search";

            string rawQuery = query["query"].ToString();
            if (string.IsNullOrWhiteSpace(rawQuery))
                rawQuery = query["q"].ToString();

            var parsed = ProwlarrQueryParser.Parse(rawQuery, type);
            if (parsed.TvdbIdOnly)
                return Json(Array.Empty<object>());

            if (string.IsNullOrWhiteSpace(parsed.Query)
                && string.IsNullOrWhiteSpace(parsed.Title)
                && string.IsNullOrWhiteSpace(parsed.TitleOriginal)
                && string.IsNullOrWhiteSpace(query["title"].ToString())
                && string.IsNullOrWhiteSpace(query["title_original"].ToString()))
                return Json(Array.Empty<object>());

            string torznabAction = type.ToLowerInvariant() switch
            {
                "tv" => "tvsearch",
                "moviesearch" => "movie",
                _ => type.ToLowerInvariant()
            };

            // Lampa Jackett uses is_serial=1 (movie) / 2 (serial). Map Prowlarr type+categories the same way.
            int isSerial = IndexerRequestParams.IsSerialFromTorznabAction(torznabAction);
            if (isSerial < 0)
                isSerial = IndexerRequestParams.IsSerialFromCategories(IndexerRequestParams.CategoriesFromQuery(query));

            string boundTitle = !string.IsNullOrWhiteSpace(query["title"].ToString())
                ? query["title"].ToString()
                : parsed.Title;
            string boundTitleOriginal = !string.IsNullOrWhiteSpace(query["title_original"].ToString())
                ? query["title_original"].ToString()
                : parsed.TitleOriginal;

            var req = IndexerSearchHelper.BuildRequest(query, apikey, rqnum: false,
                boundQuery: parsed.Query,
                boundTitle: boundTitle,
                boundTitleOriginal: boundTitleOriginal,
                boundYear: parsed.Year ?? 0,
                boundIsSerial: isSerial);

            if (parsed.Season.HasValue)
                req.Season = parsed.Season;
            if (parsed.Episode.HasValue)
                req.Episode = parsed.Episode;
            if (!string.IsNullOrWhiteSpace(parsed.Genre) && string.IsNullOrWhiteSpace(req.Genres))
                req.Genres = parsed.Genre;

            var results = await IndexerSearchEngine.SearchCombinedAsync(req, memoryCache, _searchService);
            results = IndexerSearchHelper.ApplyPostFilters(results, query, req, torznabAction);

            bool enrich = AppInit.conf.torznab?.enrichTitles ?? true;
            return Json(ProwlarrSearchFormatter.MapReleases(results, enrich));
        }
    }
}
