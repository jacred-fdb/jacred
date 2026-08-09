using System;
using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.Viruseproject
{
    sealed class ViruseprojectCategory
    {
        public string[] Types { get; init; }
        /// <summary>?start pagination step (from site pagination-end).</summary>
        public int PageStep { get; init; } = 10;
    }

    /// <summary>
    /// Single source of truth for viruseproject.tv categories, types, and page steps.
    /// Keep dry_run_viruseproject_parser.py CATEGORIES in sync.
    /// </summary>
    static class ViruseprojectCategories
    {
        public static readonly Dictionary<string, ViruseprojectCategory> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["serials"] = new() { Types = new[] { "serial" }, PageStep = 10 },
            ["movies"] = new() { Types = new[] { "movie" }, PageStep = 10 },
            ["documentary"] = new() { Types = new[] { "docuserial", "documovie" }, PageStep = 6 },
            ["cartoons"] = new() { Types = new[] { "multfilm", "multserial" }, PageStep = 6 },
            ["reality-show"] = new() { Types = new[] { "tvshow" }, PageStep = 6 },
        };

        public static IEnumerable<string> Ids => Map.Keys;
    }
}
