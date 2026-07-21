using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JacRed.Infrastructure.Tracks
{
    /// <summary>
    /// Picks TorrServer file ids (1-based, path-sorted on TS) most likely to be probeable video.
    /// </summary>
    internal static class TracksMediaFileSelector
    {
        static readonly string[] VideoExtensions =
        {
            ".mkv", ".mp4", ".avi", ".m2ts", ".ts", ".wmv", ".webm", ".mov", ".mpg", ".mpeg"
        };

        static readonly string[] ExcludedPathFragments =
        {
            ".sample.", "/sample/", "\\sample\\",
            "proof", "trailer", "preview", "screenshot"
        };

        /// <summary>
        /// Returns up to <paramref name="maxCount"/> file ids ordered by probe priority (best first).
        /// </summary>
        internal static IReadOnlyList<int> SelectFileIds(IList<TracksDB.TorrentFileStat> fileStats, int maxCount)
        {
            if (fileStats == null || fileStats.Count == 0)
                return new[] { 1 };

            int limit = Math.Max(1, maxCount);
            var ranked = fileStats
                .Where(f => f != null && f.id > 0 && !string.IsNullOrEmpty(f.path))
                .Select(f => (file: f, isVideo: IsVideoCandidate(f.path)))
                .Where(x => x.isVideo)
                .OrderByDescending(x => x.file.length)
                .ThenBy(x => x.file.path.Length)
                .ThenBy(x => x.file.id)
                .Select(x => x.file.id)
                .Distinct()
                .Take(limit)
                .ToList();

            if (ranked.Count > 0)
                return ranked;

            // Fallback: largest file by size, then id 1.
            var largest = fileStats
                .Where(f => f != null && f.id > 0)
                .OrderByDescending(f => f.length)
                .ThenBy(f => f.id)
                .Select(f => f.id)
                .FirstOrDefault();

            return largest > 0 ? new[] { largest } : new[] { 1 };
        }

        internal static bool IsVideoCandidate(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var lower = path.Replace('\\', '/').ToLowerInvariant();
            foreach (var fragment in ExcludedPathFragments)
            {
                if (lower.Contains(fragment, StringComparison.Ordinal))
                    return false;
            }

            var ext = Path.GetExtension(lower);
            return !string.IsNullOrEmpty(ext) &&
                   VideoExtensions.Contains(ext, StringComparer.Ordinal);
        }
    }
}
