using System.Threading.Tasks;

namespace JacRed.Application.Search
{
    public interface ITorrentQueryService
    {
        Task<object> QueryTorrentsAsync(string search, string altname, bool exact, string type, string sort, string tracker, string voice, string videotype, long relased, long quality, long season, Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache);

        object QueryQualitys(string name, string originalname, string type, int page, int take);
    }
}
