using System;
using System.Collections.Generic;
using System.Linq;

namespace JacRed.Infrastructure.Trackers.Bitru
{
    /// <summary>
    /// Single source of truth for BitRu API category request filters and JacRed types.
    /// Official docs: https://bitru.org/api.php
    /// </summary>
    static class BitruCategories
    {
        public static readonly string[] RequestCategories = { "movie", "serial", "video" };

        static readonly HashSet<string> NonVideoCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "music", "game", "soft", "literature", "audiobook", "image", "xxx"
        };

        static readonly string[] DocumovieSubsections =
        {
            "Документальный", "Научный", "Исторический", "Биография"
        };

        static readonly string[] SportSubsections = { "Спорт" };

        static readonly string[] TvShowSubsections = { "Шоу", "Клипы", "Концерт" };

        /// <summary>Categories we intentionally do not request or map.</summary>
        public static IEnumerable<string> NonVideoIds => NonVideoCategories;

        public static bool TryGetTypes(string category, IList<string> subsections, out string[] types)
        {
            types = null;
            string cat = (category ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(cat) || NonVideoCategories.Contains(cat))
                return false;

            if (cat == "movie")
            {
                types = new[] { "movie" };
                return true;
            }

            if (cat == "serial")
            {
                types = new[] { "serial" };
                return true;
            }

            if (cat == "video")
                return TryGetVideoTypes(subsections, out types);

            return false;
        }

        static bool TryGetVideoTypes(IList<string> subsections, out string[] types)
        {
            types = null;
            if (subsections == null || subsections.Count == 0)
                return false;

            foreach (string sub in subsections)
            {
                if (string.IsNullOrWhiteSpace(sub))
                    continue;

                if (DocumovieSubsections.Any(x => string.Equals(x, sub, StringComparison.OrdinalIgnoreCase)))
                {
                    types = new[] { "documovie" };
                    return true;
                }

                if (SportSubsections.Any(x => string.Equals(x, sub, StringComparison.OrdinalIgnoreCase)))
                {
                    types = new[] { "sport" };
                    return true;
                }

                if (TvShowSubsections.Any(x => string.Equals(x, sub, StringComparison.OrdinalIgnoreCase)))
                {
                    types = new[] { "tvshow" };
                    return true;
                }
            }

            // Трейлер, Эротика, Уроки, Детское, unknown → drop
            return false;
        }
    }
}
