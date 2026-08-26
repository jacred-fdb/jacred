using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.Anistar
{
    sealed class AnistarCategory
    {
        public string[] Types { get; init; }
    }

    /// <summary>
    /// Single source of truth for Anistar (v30.astar.bz) section paths and JacRed types.
    /// </summary>
    static class AnistarCategories
    {
        public static readonly Dictionary<string, AnistarCategory> Map = new()
        {
            ["anime"] = new() { Types = new[] { "anime" } },
            ["hentai"] = new() { Types = new[] { "anime" } },
            ["dorams"] = new() { Types = new[] { "serial" } },
        };

        public static IEnumerable<string> Ids => Map.Keys;
    }
}
