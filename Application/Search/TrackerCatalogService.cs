using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Configuration.Schema;

namespace JacRed.Application.Search
{
    public class TrackerCatalogService : ITrackerCatalogService
    {
        public Task<IReadOnlyList<string>> GetTrackerNamesAsync()
        {
            // synctrackers set (including empty = intentionally none) → config list.
            // synctrackers null → known built-in slugs (avoids O(all torrents) FileDB scan).
            IEnumerable<string> source = AppInit.conf.synctrackers != null
                ? AppInit.conf.synctrackers
                : ConfigSchema.KnownTrackerSlugs;

            return Task.FromResult(FromConfigured(source));
        }

        static IReadOnlyList<string> FromConfigured(IEnumerable<string> source)
        {
            var disabled = new HashSet<string>(
                AppInit.conf.disable_trackers ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            return source
                .Where(i => !string.IsNullOrWhiteSpace(i) && !disabled.Contains(i.Trim()))
                .Select(i => i.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(i => i, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
