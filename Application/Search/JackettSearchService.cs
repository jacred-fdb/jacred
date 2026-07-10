using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Application.Index;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Application.Search
{
    public class JackettSearchService : IJackettSearchService
    {
        readonly IFastDbIndex _fastDbIndex;

        public JackettSearchService(IFastDbIndex fastDbIndex)
        {
            _fastDbIndex = fastDbIndex;
        }

        public async Task<List<Result>> SearchAsync(JackettSearchRequest request, IMemoryCache cache, CancellationToken ct = default)
        {
            var q = request.Query;
            bool rqnum = !request.QueryStringValue.Contains("&is_serial=")
                && request.UserAgent == "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36";

            string query = request.QueryText;
            string title = request.Title;
            string title_original = request.TitleOriginal;
            int year = request.Year;
            int is_serial = request.IsSerial;

            if (string.IsNullOrWhiteSpace(query))
                query = IndexerRequestParams.ResolveSearchQuery(q);

            if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(title_original))
                return new List<Result>();

            var req = IndexerSearchHelper.BuildRequest(q, request.ApiKey, rqnum, query, title, title_original, year, is_serial);
            var results = await IndexerSearchEngine.SearchCombinedAsync(req, cache, this);
            return IndexerSearchHelper.ApplyPostFilters(results, q, req);
        }

        public List<Result> SearchResults(string apikey, string query, string title, string title_original, int year, Dictionary<string, string> category, int is_serial, bool rqnum, IMemoryCache memoryCache)
        {
            string cachekey = $"api:v2.0:indexers:{query}:{title}:{title_original}:{year}:{(category != null && category.Count > 0 ? string.Join(",", category.Select(i => $"{i.Key}={i.Value}")) : "null")}:{is_serial}";
            if (memoryCache != null && memoryCache.TryGetValue(cachekey, out List<Result> _cacheResult))
                return _cacheResult;

            var torrents = JackettCardMatcher.Search(_fastDbIndex, query, title, title_original, year, category, is_serial, rqnum, memoryCache);
            var results = JackettResultBuilder.Build(torrents, apikey, rqnum);

            if (memoryCache != null && AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour == 0)
                memoryCache.Set(cachekey, results, System.DateTime.Now.AddMinutes(5));

            return results;
        }
    }
}
