using JacRed.Models.AppConf;

namespace JacRed.Infrastructure.Indexers
{
    public static class IndexerSearchOptions
    {
        public static SearchSettings Resolve() =>
            AppInit.conf.search ?? new SearchSettings();
    }
}
