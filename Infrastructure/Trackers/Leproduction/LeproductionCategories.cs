using System;
using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.Leproduction
{
    sealed class LeproductionCategory
    {
        public string[] Types { get; init; }
    }

    /// <summary>
    /// Single source of truth for le-production.tv category slugs and JacRed types.
    /// Keep dry_run_leproduction_parser.py CATEGORIES in sync.
    /// </summary>
    static class LeproductionCategories
    {
        public static readonly Dictionary<string, LeproductionCategory> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["anime"] = new() { Types = new[] { "anime" } },
            ["dorama"] = new() { Types = new[] { "serial" } },
            ["film"] = new() { Types = new[] { "movie" } },
            ["serial"] = new() { Types = new[] { "serial" } },
            ["fulcartoon"] = new() { Types = new[] { "multfilm" } },
            ["cartoon"] = new() { Types = new[] { "multserial" } },
        };

        public static IEnumerable<string> Ids => Map.Keys;
    }
}
