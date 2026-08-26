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
        public string TmdbId { get; init; }

        public static AllohaResolveResult Unresolved(string search, string altname) => new AllohaResolveResult
        {
            Search = search,
            AltName = altname
        };
    }

    /// <summary>
    /// Alloha TV API v2: resolve <c>tt…</c> / <c>kp…</c> / <c>tmdb…</c> (and TMDB URLs) to titles for FileDB search.
    /// </summary>
    public static class AllohaTitleResolver
    {
        static readonly Regex CompactIdPattern = new Regex(
            @"^(?:tt|kp|tmdb:?)\d+$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>themoviedb.org/movie|tv/{id} or /{id}-{slug}</summary>
        static readonly Regex TmdbUrlPattern = new Regex(
            @"themoviedb\.org/(?<kind>movie|tv)/(?<id>\d+)(?:-[^\s/?#]*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsResolvableId(string search) =>
            TryNormalizeId(search, out _, out _);

        /// <summary>Backward-compatible alias for <see cref="IsResolvableId"/>.</summary>
        public static bool IsImdbOrKpId(string search) => IsResolvableId(search);

        /// <summary>
        /// Normalize <c>tt…</c>/<c>kp…</c>/<c>tmdb…</c>/TMDB URL to a canonical id.
        /// </summary>
        /// <param name="raw">User input (id or themoviedb.org URL).</param>
        /// <param name="canonicalId">Normalized id such as <c>tmdb1315772</c>.</param>
        /// <param name="urlCategoryHint">movie or serial from TMDB URL path when present.</param>
        public static bool TryNormalizeId(string raw, out string canonicalId, out string urlCategoryHint)
        {
            canonicalId = null;
            urlCategoryHint = null;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string s = raw.Trim();

            var url = TmdbUrlPattern.Match(s);
            if (url.Success)
            {
                canonicalId = "tmdb" + url.Groups["id"].Value;
                string kind = url.Groups["kind"].Value;
                urlCategoryHint = kind.Equals("tv", StringComparison.OrdinalIgnoreCase) ? "serial"
                    : kind.Equals("movie", StringComparison.OrdinalIgnoreCase) ? "movie"
                    : null;
                return true;
            }

            if (CompactIdPattern.IsMatch(s))
            {
                if (s.StartsWith("tmdb", StringComparison.OrdinalIgnoreCase))
                {
                    string digits = s.Substring(4).TrimStart(':');
                    if (!Regex.IsMatch(digits, @"^\d+$"))
                        return false;
                    canonicalId = "tmdb" + digits;
                    return true;
                }

                canonicalId = s.ToLowerInvariant();
                return true;
            }

            return false;
        }

        /// <summary>
        /// If <paramref name="search"/> is an external id / TMDB URL, resolve via Alloha; otherwise return unchanged.
        /// </summary>
        public static async Task<AllohaResolveResult> ResolveAsync(
            string search, string altname, IMemoryCache memoryCache)
        {
            if (!TryNormalizeId(search, out string id, out string urlCategoryHint))
                return AllohaResolveResult.Unresolved(search, altname);

            var conf = AppInit.conf.alloha;
            if (conf == null || !conf.enable || string.IsNullOrWhiteSpace(conf.token))
                return AllohaResolveResult.Unresolved(search, altname);

            string memkey = CacheKey(id);

            if (memoryCache != null && memoryCache.TryGetValue(memkey, out AllohaResolveResult cached) && cached != null)
            {
                var fromCache = WithFallback(cached, search, altname);
                if (string.IsNullOrWhiteSpace(fromCache.Type) && !string.IsNullOrWhiteSpace(urlCategoryHint))
                {
                    return new AllohaResolveResult
                    {
                        Search = fromCache.Search,
                        AltName = fromCache.AltName,
                        AlternativeName = fromCache.AlternativeName,
                        Year = fromCache.Year,
                        Type = urlCategoryHint,
                        ImdbId = fromCache.ImdbId,
                        KpId = fromCache.KpId,
                        TmdbId = fromCache.TmdbId ?? id
                    };
                }
                return fromCache;
            }

            string baseUrl = (conf.baseUrl ?? "https://apbugall.org").TrimEnd('/');
            string query;
            if (id.StartsWith("kp", StringComparison.OrdinalIgnoreCase))
                query = $"kp={id.Substring(2)}";
            else if (id.StartsWith("tmdb", StringComparison.OrdinalIgnoreCase))
                query = $"tmdb={id.Substring(4)}";
            else
                query = $"imdb={id}";

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
            string tmdbId = NormalizeTmdbId(ids?.Value<object>("tmdb")) ?? (id.StartsWith("tmdb", StringComparison.OrdinalIgnoreCase) ? id : null);

            var mapped = MapTitles(originalName, name, alternativeName, year, categorySlug, urlCategoryHint,
                imdbId, kpId, tmdbId, search, altname);

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

            if (!string.IsNullOrWhiteSpace(result.TmdbId))
                memoryCache.Set(CacheKey(result.TmdbId), result, expiry);
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

        static string NormalizeTmdbId(object tmdb)
        {
            if (tmdb == null)
                return null;
            string s = tmdb.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(s) || !Regex.IsMatch(s, "^[0-9]+$"))
                return null;
            return "tmdb" + s;
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
            string originalName, string name, string alternativeName, int year, string categorySlug, string urlCategoryHint,
            string imdbId, string kpId, string tmdbId, string fallbackSearch, string fallbackAlt)
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

            string type = MapCategorySlug(categorySlug) ?? urlCategoryHint;

            return new AllohaResolveResult
            {
                Search = search,
                AltName = alt,
                AlternativeName = altNameDistinct,
                Year = year,
                Type = type,
                ImdbId = imdbId,
                KpId = kpId,
                TmdbId = tmdbId
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
