using System;
using System.Collections.Generic;
using System.Linq;

namespace JacRed.Infrastructure.Trackers
{
    /// <summary>
    /// Trackers whose pages report inflated seeder/peer counts.
    /// When duplicates are merged, their counts are used only if no other tracker has the torrent.
    /// </summary>
    public static class UnreliableSeeders
    {
        static readonly HashSet<string> Trackers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "selezen", "korsars" };

        /// <summary>True if any tracker in a ", "-separated list (merged results) reports reliable counts.</summary>
        public static bool AnyReliable(string trackerNames)
        {
            if (string.IsNullOrWhiteSpace(trackerNames))
                return true;

            return trackerNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(name => !Trackers.Contains(name));
        }
    }
}
