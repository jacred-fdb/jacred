using System;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Infrastructure.Caching
{
    public static class MemoryCacheExtensions
    {
        /// <summary>Set cache entry with Size=1 so MemoryCache SizeLimit compaction works.</summary>
        public static void SetSized<T>(this IMemoryCache cache, object key, T value, DateTimeOffset absoluteExpiration)
        {
            cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = absoluteExpiration,
                Size = 1
            });
        }

        public static void SetSized<T>(this IMemoryCache cache, object key, T value, TimeSpan absoluteExpirationRelativeToNow)
        {
            cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow,
                Size = 1
            });
        }
    }
}
