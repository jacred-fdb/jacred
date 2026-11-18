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

        /// <summary>
        /// The releases for a card (<paramref name="title"/>, <paramref name="title_original"/>) or, without one, for a text
        /// <paramref name="query"/>. <paramref name="nameFound"/> says whether a release was found by a name itself, not only by
        /// the name with episodes in brackets behind it ("Холод (1-10 из 10)"): those must not stand in for the search
        /// without the year, which finds the other seasons and spellings. With <paramref name="franchise"/> (card fields only),
        /// instead of the exact names, the releases of the card year that carry the title behind a franchise prefix
        /// ("Джеймс Бонд 007: Голдфингер").
        /// </summary>
        List<Result> SearchResults(string apikey, string query, string title, string title_original, int year, Dictionary<string, string> category, int is_serial, bool rqnum, IMemoryCache memoryCache, out bool nameFound, bool franchise = false);
    }
}
