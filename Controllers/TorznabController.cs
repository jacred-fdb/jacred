using JacRed.Application.Index;
using JacRed.Application.Search;
using JacRed.Engine;
using JacRed.Engine.Indexers;
using JacRed.Models.Api;
using JacRed.Models.AppConf;
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
        readonly IFastDbIndex _fastDbIndex;
        readonly IJackettSearchService _searchService;

        public TorznabController(IMemoryCache memoryCache, IFastDbIndex fastDbIndex, IJackettSearchService searchService) : base(memoryCache)
        {
            _fastDbIndex = fastDbIndex;
            _searchService = searchService;
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
                    return Content(TorznabXmlFormatter.IndexersXml, "application/xml; charset=utf-8");
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
            var results = await IndexerSearchEngine.SearchCombinedAsync(req, memoryCache, _fastDbIndex, _searchService);
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

    /// <summary>Jackett/Prowlarr metadata endpoints.</summary>
    public class JackettMetaController : BaseController
    {
        public JackettMetaController(IMemoryCache memoryCache) : base(memoryCache) { }

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
    }
}
