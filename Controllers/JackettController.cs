using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using JacRed.Application.Search;
using JacRed.Infrastructure.Security;
using JacRed.Models.Api;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers
{
    public class JackettController : BaseController
    {
        readonly IJackettSearchService _searchService;

        public JackettController(IMemoryCache memoryCache, IJackettSearchService searchService) : base(memoryCache)
        {
            _searchService = searchService;
        }

        #region Jackett
        [Route("/api/v2.0/indexers/{status}/results")]
        async public Task<ActionResult> Jackett(string status, string apikey, string query, string title, string title_original, int year, Dictionary<string, string> category, int is_serial = -1)
        {
            var request = new JackettSearchRequest
            {
                Query = HttpContext.Request.Query,
                QueryStringValue = HttpContext.Request.QueryString.Value ?? "",
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString(),
                ApiKey = string.IsNullOrWhiteSpace(apikey)
                    ? JacRedKeyUtils.GetApiKeyFromRequest(HttpContext)
                    : apikey,
                QueryText = query,
                Title = title,
                TitleOriginal = title_original,
                Year = year,
                IsSerial = is_serial,
                IndexerPath = status
            };

            var results = await _searchService.SearchAsync(request, memoryCache);
            return Json(new RootObject() { Results = results });
        }
        #endregion
    }
}
