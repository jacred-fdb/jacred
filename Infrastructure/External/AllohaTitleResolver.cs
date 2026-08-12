using JacRed.Infrastructure.Networking;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.External
{
    public sealed class AllohaResolveResult
    {
        public string Search { get; init; }
        public string AltName { get; init; }
        public string AlternativeName { get; init; }
        public int Year { get; init; }
        /// <summary>JacRed FileDB type hint from Alloha category.slug (movie/serial/anime).</summary>
        public string Type { get; init; }
        public string ImdbId { get; init; }
        public string KpId { get; init; }

        public static AllohaResolveResult Unresolved(string search, string altname) => new AllohaResolveResult
        {
            Search = search,
            AltName = altname
        };
    }

    /// <summary>
    /// Alloha TV API v2: resolve <c>tt…</c> / <c>kp…</c> to titles (+ year/type) for FileDB search.
    /// </summary>
    public static class AllohaTitleResolver
    {
        static readonly Regex IdPattern = new Regex("^(tt|kp)[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsImdbOrKpId(string search) =>
            !string.IsNullOrWhiteSpace(search) && IdPattern.IsMatch(search.Trim());

        /// <summary>
        /// If <paramref name="search"/> is an IMDb/KP id, resolve via Alloha; otherwise return inputs unchanged.
        /// </summary>
        public static async Task<AllohaResolveResult> ResolveAsync(
            string search, string altname, IMemoryCache memoryCache)
        {
            if (!IsImdbOrKpId(search))
                return AllohaResolveResult.Unresolved(search, altname);

            var conf = AppInit.conf.alloha;
            if (conf == null || !conf.enable || string.IsNullOrWhiteSpace(conf.token))
                return AllohaResolveResult.Unresolved(search, altname);

            string id = search.Trim();
            string memkey = CacheKey(id);

            if (memoryCache != null && memoryCache.TryGetValue(memkey, out AllohaResolveResult cached) && cached != null)
                return WithFallback(cached, search, altname);

            string baseUrl = (conf.baseUrl ?? "https://apbugall.org").TrimEnd('/');
            string query = id.StartsWith("kp", StringComparison.OrdinalIgnoreCase)
                ? $"kp={id.Substring(2)}"
                : $"imdb={id}";

            var headers = new List<(string name, string val)>
            {
                ("Authorization", $"Bearer {conf.token}")
            };

            int timeout = conf.timeoutSeconds > 0 ? conf.timeoutSeconds : 8;
            var root = await HttpClient.Get<JObject>(
                $"{baseUrl}/v2/movies/search?{query}",
                timeoutSeconds: timeout,
                addHeaders: headers);

            var data = root?.Value<JObject>("data");
            string originalName = data?.Value<string>("original_name");
            string name = data?.Value<string>("name");
            string alternativeName = data?.Value<string>("alternative_name");
            int year = data?.Value<int?>("year") ?? 0;
            string categorySlug = data?.Value<JObject>("category")?.Value<string>("slug");
            var ids = data?.Value<JObject>("ids");
            string imdbId = NormalizeImdbId(ids?.Value<string>("imdb"));
            string kpId = NormalizeKpId(ids?.Value<object>("kp"));

            var mapped = MapTitles(originalName, name, alternativeName, year, categorySlug, imdbId, kpId, search, altname);

            int cacheHours = conf.cacheHours > 0 ? conf.cacheHours : 24;
            var expiry = DateTime.Now.AddHours(cacheHours);
            CacheResult(memoryCache, mapped, memkey, expiry);

            return mapped;
        }

        static void CacheResult(IMemoryCache memoryCache, AllohaResolveResult result, string queryKey, DateTime expiry)
        {
            if (memoryCache == null || result == null)
                return;

            memoryCache.Set(queryKey, result, expiry);

            if (!string.IsNullOrWhiteSpace(result.ImdbId))
                memoryCache.Set(CacheKey(result.ImdbId), result, expiry);

            if (!string.IsNullOrWhiteSpace(result.KpId))
                memoryCache.Set(CacheKey(result.KpId), result, expiry);
        }

        static string CacheKey(string id) => $"alloha:title:{id.Trim().ToLowerInvariant()}";

        static string NormalizeImdbId(string imdb)
        {
            if (string.IsNullOrWhiteSpace(imdb))
                return null;
            imdb = imdb.Trim();
            if (imdb.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                return imdb.ToLowerInvariant();
            if (Regex.IsMatch(imdb, "^[0-9]+$"))
                return "tt" + imdb;
            return null;
        }

        static string NormalizeKpId(object kp)
        {
            if (kp == null)
                return null;
            string s = kp.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(s) || !Regex.IsMatch(s, "^[0-9]+$"))
                return null;
            return "kp" + s;
        }

        static string MapCategorySlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;
            switch (slug.Trim().ToLowerInvariant())
            {
                case "movie":
                    return "movie";
                case "serial":
                case "tv-show":
                    return "serial";
                case "anime":
                case "anime-serial":
                    return "anime";
                default:
                    return null;
            }
        }

        static AllohaResolveResult MapTitles(
            string originalName, string name, string alternativeName, int year, string categorySlug,
            string imdbId, string kpId, string fallbackSearch, string fallbackAlt)
        {
            string search;
            string alt;

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(originalName))
            {
                search = originalName;
                alt = name;
            }
            else
            {
                string resolved = originalName ?? name;
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    search = resolved;
                    alt = fallbackAlt;
                }
                else
                {
                    search = fallbackSearch;
                    alt = fallbackAlt;
                }
            }

            string altNameDistinct = null;
            if (!string.IsNullOrWhiteSpace(alternativeName))
            {
                string a = alternativeName.Trim();
                if (!string.Equals(a, search, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(a, alt, StringComparison.OrdinalIgnoreCase))
                    altNameDistinct = a;
            }

            return new AllohaResolveResult
            {
                Search = search,
                AltName = alt,
                AlternativeName = altNameDistinct,
                Year = year,
                Type = MapCategorySlug(categorySlug),
                ImdbId = imdbId,
                KpId = kpId
            };
        }

        static AllohaResolveResult WithFallback(AllohaResolveResult cached, string fallbackSearch, string fallbackAlt)
        {
            if (!string.IsNullOrWhiteSpace(cached.Search))
                return cached;
            return AllohaResolveResult.Unresolved(fallbackSearch, fallbackAlt);
        }
    }
}
