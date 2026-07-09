using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Models.Api;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Application.Search
{
    public interface IJackettSearchService
    {
        Task<List<Result>> SearchAsync(JackettSearchRequest request, IMemoryCache cache, CancellationToken ct = default);

        List<Result> SearchResults(string apikey, string query, string title, string title_original, int year, Dictionary<string, string> category, int is_serial, bool rqnum, IMemoryCache memoryCache);
    }
}
