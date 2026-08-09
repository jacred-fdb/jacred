using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.Anifilm
{
    sealed class AnifilmCategory
    {
        public string[] Types { get; init; }
        public int FullMax { get; init; }
        public int QuickMax { get; init; } = 2;
    }

    /// <summary>
    /// Single source of truth for anifilm.pro category slugs, types, and page limits.
    /// Keep dry_run_anifilm_parser.py CATEGORIES in sync.
    /// </summary>
    static class AnifilmCategories
    {
        public static readonly Dictionary<string, AnifilmCategory> Map = new()
        {
            ["serials"] = new() { Types = new[] { "anime" }, FullMax = 70, QuickMax = 2 },
            ["ova"] = new() { Types = new[] { "anime" }, FullMax = 32, QuickMax = 2 },
            ["ona"] = new() { Types = new[] { "anime" }, FullMax = 2, QuickMax = 2 },
            ["movies"] = new() { Types = new[] { "anime" }, FullMax = 17, QuickMax = 2 },
            ["dorams"] = new() { Types = new[] { "serial" }, FullMax = 10, QuickMax = 2 },
            ["special"] = new() { Types = new[] { "anime" }, FullMax = 5, QuickMax = 2 },
            ["hentai"] = new() { Types = new[] { "anime" }, FullMax = 5, QuickMax = 2 },
            ["short-serials"] = new() { Types = new[] { "anime" }, FullMax = 5, QuickMax = 2 },
        };

        public static IEnumerable<string> Ids => Map.Keys;

        public static int MaxPages(AnifilmCategory cat, bool fullparse) =>
            fullparse ? cat.FullMax : cat.QuickMax;
    }
}
