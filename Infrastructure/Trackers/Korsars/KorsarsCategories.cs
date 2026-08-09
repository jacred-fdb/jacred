using System.Collections.Generic;
using System.Linq;

namespace JacRed.Infrastructure.Trackers.Korsars
{
    sealed class KorsarsCategory
    {
        public string[] Types { get; init; }
    }

    /// <summary>
    /// Single source of truth for korsars.pro forum IDs and JacRed types
    /// (hand-picked movie/serial/cartoon sections from Go cron/korsars).
    /// Keep dry_run_korsars_parser.py cat lists in sync.
    /// </summary>
    static class KorsarsCategories
    {
        public static readonly IReadOnlyList<string> MovieIds = new[]
        {
            "282", "31", "33", "125", "146", "270"
        };

        public static readonly IReadOnlyList<string> SerialIds = new[]
        {
            "287", "286", "267", "303", "288", "39", "40", "300", "41", "121", "144", "271"
        };

        public static readonly IReadOnlyList<string> CartoonIds = new[]
        {
            "43", "44", "277", "46", "272", "273"
        };

        public static readonly Dictionary<string, KorsarsCategory> Map;

        static KorsarsCategories()
        {
            var map = new Dictionary<string, KorsarsCategory>();
            foreach (string c in MovieIds)
                map[c] = new() { Types = new[] { "movie" } };
            foreach (string c in SerialIds)
                map[c] = new() { Types = new[] { "serial" } };
            // Cartoon forums mix films and series — emit both types.
            foreach (string c in CartoonIds)
                map[c] = new() { Types = new[] { "multfilm", "multserial" } };
            Map = map;
        }

        public static IEnumerable<string> Ids => MovieIds.Concat(SerialIds).Concat(CartoonIds);

        public static string[] TypesFor(string cat)
        {
            if (string.IsNullOrWhiteSpace(cat) || !Map.TryGetValue(cat, out var meta))
                return new[] { "movie" };
            return meta.Types;
        }
    }
}
