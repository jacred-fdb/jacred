using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.Ultradox
{
    sealed class UltradoxCategory
    {
        public string[] Types { get; init; }
    }

    /// <summary>
    /// Single source of truth for ultradox.onl section paths and JacRed types.
    /// Keep dry_run_ultradox_parser.py in sync.
    /// </summary>
    static class UltradoxCategories
    {
        public static readonly Dictionary<string, UltradoxCategory> Map = new()
        {
            ["serial-hd"] = new() { Types = new[] { "serial" } },
            ["hd"] = new() { Types = new[] { "movie" } },
            ["rufilm"] = new() { Types = new[] { "movie" } },
            ["camrip"] = new() { Types = new[] { "movie" } },
            ["webrips"] = new() { Types = new[] { "movie" } },
            ["anime"] = new() { Types = new[] { "anime" } },
        };

        public static IEnumerable<string> Ids => Map.Keys;
    }
}
