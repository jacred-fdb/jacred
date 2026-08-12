using JacRed.Infrastructure.Networking;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.External
{
    /// <summary>
    /// Alloha TV API v2: resolve <c>tt…</c> / <c>kp…</c> to bilingual titles (+ year) for FileDB search.
    /// </summary>
    public static class AllohaTitleResolver
    {
        static readonly Regex IdPattern = new Regex("^(tt|kp)[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsImdbOrKpId(string search) =>
            !string.IsNullOrWhiteSpace(search) && IdPattern.IsMatch(search.Trim());

        /// <summary>
        /// If <paramref name="search"/> is an IMDb/KP id, resolve via Alloha; otherwise return inputs unchanged (year 0).
        /// </summary>
        public static async Task<(string search, string altname, int year)> ResolveAsync(
            string search, string altname, IMemoryCache memoryCache)
        {
            if (!IsImdbOrKpId(search))
                return (search, altname, 0);

            var conf = AppInit.conf.alloha;
            if (conf == null || !conf.enable || string.IsNullOrWhiteSpace(conf.token))
                return (search, altname, 0);

            string id = search.Trim();
            string memkey = $"alloha:title:{id.ToLowerInvariant()}";

            if (memoryCache != null && memoryCache.TryGetValue(memkey, out (string original_name, string name, int year) cached))
                return MapTitles(cached.original_name, cached.name, cached.year, search, altname);

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

            string originalName = root?.Value<JObject>("data")?.Value<string>("original_name");
            string name = root?.Value<JObject>("data")?.Value<string>("name");
            int year = root?.Value<JObject>("data")?.Value<int?>("year") ?? 0;

            var entry = (originalName, name, year);
            int cacheHours = conf.cacheHours > 0 ? conf.cacheHours : 24;
            memoryCache?.Set(memkey, entry, DateTime.Now.AddHours(cacheHours));

            return MapTitles(originalName, name, year, search, altname);
        }

        static (string search, string altname, int year) MapTitles(
            string originalName, string name, int year, string fallbackSearch, string fallbackAlt)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(originalName))
                return (originalName, name, year);

            string resolved = originalName ?? name;
            if (!string.IsNullOrWhiteSpace(resolved))
                return (resolved, fallbackAlt, year);

            return (fallbackSearch, fallbackAlt, year);
        }
    }
}
