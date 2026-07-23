using System.Collections.Generic;
using System.Threading.Tasks;

namespace JacRed.Application.Search
{
    public interface ITrackerCatalogService
    {
        Task<IReadOnlyList<string>> GetTrackerNamesAsync();
    }
}
