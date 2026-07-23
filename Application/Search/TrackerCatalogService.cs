using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Application.Search
{
    public class TrackerCatalogService : ITrackerCatalogService
    {
        const string CacheKey = "api:trackers:names";
        static readonly TimeSpan CacheDuration = TimeSpan.FromHours(4);
        readonly IMemoryCache _memoryCache;

        public TrackerCatalogService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        public Task<IReadOnlyList<string>> GetTrackerNamesAsync()
        {
            if (AppInit.conf.synctrackers != null)
            {
                var disabled = new HashSet<string>(
                    AppInit.conf.disable_trackers ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

                return Task.FromResult<IReadOnlyList<string>>(AppInit.conf.synctrackers
                    .Where(i => !string.IsNullOrWhiteSpace(i) && !disabled.Contains(i))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
            }

            if (_memoryCache.TryGetValue(CacheKey, out IReadOnlyList<string> cached))
                return Task.FromResult(cached);

            var trackers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var shard in FileDB.masterDb.Keys)
            {
                foreach (var torrent in FileDB.OpenRead(shard, true).Values)
                {
                    if (string.IsNullOrWhiteSpace(torrent?.trackerName))
                        continue;

                    foreach (var tracker in torrent.trackerName.Split(','))
                    {
                        var name = tracker.Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            trackers.Add(name);
                    }
                }
            }

            var result = trackers.OrderBy(i => i, StringComparer.Ordinal).ToList();
            _memoryCache.Set(CacheKey, result, CacheDuration);
            return Task.FromResult<IReadOnlyList<string>>(result);
        }
    }
}
