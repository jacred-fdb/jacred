using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using JacRed.Application.Search;
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
        async public Task<ActionResult> Jackett(string apikey, string query, string title, string title_original, int year, Dictionary<string, string> category, int is_serial = -1)
        {
            var request = new JackettSearchRequest
            {
                Query = HttpContext.Request.Query,
                QueryStringValue = HttpContext.Request.QueryString.Value ?? "",
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString(),
                ApiKey = apikey,
                QueryText = query,
                Title = title,
                TitleOriginal = title_original,
                Year = year,
                IsSerial = is_serial
            };

            var results = await _searchService.SearchAsync(request, memoryCache);
            return Json(new RootObject() { Results = results });
        }
        #endregion
    }
}
