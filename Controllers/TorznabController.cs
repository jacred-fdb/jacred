using JacRed.Engine;
using JacRed.Engine.Indexers;
using JacRed.Models.Api;
using JacRed.Models.AppConf;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JacRed.Controllers
{
    /// <summary>
    /// Native Torznab XML API (jacred-proxy compatibility without external worker).
    /// </summary>
    public class TorznabController : BaseController
    {
        [Route("/torznab/api")]
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

            var req = IndexerSearchHelper.BuildRequest(query, apikey, rqnum: false, boundQuery: resolvedQuery);
            var results = await IndexerSearchEngine.SearchCombinedAsync(req, memoryCache);
            results = IndexerSearchHelper.ApplyPostFilters(results, query, req, t);
            return XmlSearchResult(results, t, query, origin);
        }

        IActionResult XmlSearchResult(List<Result> results, string t, IQueryCollection query, string origin)
        {
            string assignedCat = "";
            string catParam = IndexerSearchHelper.CategoryParam(query);
            if (t == "tvsearch" || t == "tv") assignedCat = "5000";
            else if (t == "moviesearch" || t == "movie") assignedCat = "2000";
            else if (!string.IsNullOrWhiteSpace(catParam)) assignedCat = catParam.Split(',')[0].Trim();

            bool enrich = AppInit.conf.torznab?.enrichTitles ?? true;
            string items = TorznabXmlFormatter.ItemsXml(results, assignedCat, enrich, catParam);
            return Content(TorznabXmlFormatter.WrapRss(items, origin), "application/xml; charset=utf-8");
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
