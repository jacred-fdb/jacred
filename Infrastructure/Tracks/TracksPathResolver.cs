using JacRed.Models.Tracks;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;

namespace JacRed.Infrastructure.Tracks
{
    internal static class TracksPathResolver
    {
        /// <summary>
        /// Canonical layout (JacRed + lampa-tracks): {aa}/{b}/{hash}.json — lowercase hex.
        /// </summary>
        internal static string TrackLayoutPath(string tracksDir, string infohash, bool withExtension = true)
        {
            infohash = NormalizeInfohash(infohash);
            if (!IsValidInfohash(infohash))
                throw new ArgumentException("Invalid infohash.", nameof(infohash));

            string folder = Path.Combine(tracksDir, infohash.Substring(0, 2), infohash[2].ToString());
            string filename = withExtension ? $"{infohash.Substring(3)}.json" : infohash.Substring(3);
            return Path.Combine(folder, filename);
        }

        /// <summary>
        /// Legacy uppercase layout — read fallback only.
        /// </summary>
        internal static string UppercaseLayoutPath(string tracksDir, string infohash, bool withExtension = true)
        {
            infohash = NormalizeInfohash(infohash);
            if (!IsValidInfohash(infohash))
                throw new ArgumentException("Invalid infohash.", nameof(infohash));

            var upper = infohash.ToUpperInvariant();
            string folder = Path.Combine(tracksDir, upper.Substring(0, 2), upper.Substring(2, 1));
            string filename = withExtension ? $"{upper.Substring(3)}.json" : upper.Substring(3);
            return Path.Combine(folder, filename);
        }

        internal static string pathDb(string infohash, bool createfolder = false)
        {
            string path = TrackLayoutPath("Data/tracks", infohash, withExtension: true);

            if (createfolder)
                Directory.CreateDirectory(Path.GetDirectoryName(path));

            return path;
        }

        internal static bool IsLegacyTrackFile(string filename) =>
            !filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        internal static bool ShouldSkipLegacyTrackFile(string folder2Path, string filename, string tracksDir = "Data/tracks")
        {
            if (!IsLegacyTrackFile(filename))
                return false;

            if (File.Exists(Path.Combine(folder2Path, $"{filename}.json")))
                return true;

            string infohash = InfohashFromTrackRelPath(
                Path.GetFileName(Path.GetDirectoryName(folder2Path)),
                Path.GetFileName(folder2Path),
                filename);

            if (IsValidInfohash(infohash) && File.Exists(TrackLayoutPath(tracksDir, infohash, withExtension: true)))
                return true;

            return false;
        }

        internal static string ResolveTrackJsonPath(string infohash, string tracksDir = "Data/tracks")
        {
            string jsonPath = TrackLayoutPath(tracksDir, infohash, withExtension: true);
            if (File.Exists(jsonPath))
                return jsonPath;

            jsonPath = UppercaseLayoutPath(tracksDir, infohash, withExtension: true);
            if (File.Exists(jsonPath))
                return jsonPath;

            return null;
        }

        internal static string ResolveLegacyTrackPath(string infohash, string tracksDir = "Data/tracks")
        {
            string legacyPath = TrackLayoutPath(tracksDir, infohash, withExtension: false);
            if (File.Exists(legacyPath))
                return legacyPath;

            legacyPath = UppercaseLayoutPath(tracksDir, infohash, withExtension: false);
            if (File.Exists(legacyPath))
                return legacyPath;

            return null;
        }

        internal static string ResolveTrackPath(string infohash)
        {
            string jsonPath = ResolveTrackJsonPath(infohash);
            if (jsonPath != null)
                return jsonPath;

            return ResolveLegacyTrackPath(infohash);
        }

        /// <summary>
        /// Legacy без .json → canonical .json; uppercase/mixed paths → lowercase canonical layout.
        /// </summary>
        internal static int MigrateTrackLayoutInPlace(string tracksDir, bool dryRun)
        {
            if (!Directory.Exists(tracksDir))
                return 0;

            int migrated = 0;

            foreach (var folder1 in Directory.GetDirectories(tracksDir))
            {
                foreach (var folder2 in Directory.GetDirectories(folder1))
                {
                    foreach (var file in Directory.GetFiles(folder2))
                    {
                        string filename = Path.GetFileName(file);
                        if (ShouldSkipLegacyTrackFile(folder2, filename))
                            continue;

                        string infohash = InfohashFromTrackRelPath(
                            Path.GetFileName(folder1),
                            Path.GetFileName(folder2),
                            filename);

                        if (!IsValidInfohash(infohash))
                            continue;

                        string targetPath = TrackLayoutPath(tracksDir, infohash, withExtension: true);
                        if (string.Equals(file, targetPath, StringComparison.Ordinal))
                            continue;

                        if (File.Exists(targetPath))
                        {
                            if (!dryRun)
                            {
                                try { File.Delete(file); }
                                catch { }
                            }

                            migrated++;
                            continue;
                        }

                        if (!dryRun)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                            File.Move(file, targetPath);
                        }

                        migrated++;
                    }
                }
            }

            return migrated;
        }

        internal static bool IsValidInfohash(string infohash) =>
            !string.IsNullOrEmpty(infohash) && infohash.Length == 40 && infohash.All(c =>
                (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

        internal static string NormalizeInfohash(string infohash) => infohash?.ToLowerInvariant();

        internal static bool IsPathWithinDirectory(string rootDirectory, string fullPath)
        {
            try
            {
                var root = Path.GetFullPath(rootDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var path = Path.GetFullPath(fullPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static string InfohashFromTrackRelPath(string prefix2, string prefix1, string filename)
        {
            var stem = filename;
            if (stem.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                stem = stem.Substring(0, stem.Length - 5);

            return NormalizeInfohash(prefix2 + prefix1 + stem);
        }

        internal static bool TrackFileHasStreams(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                using var reader = new JsonTextReader(new StreamReader(fs));

                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.PropertyName ||
                        !string.Equals((string)reader.Value, "streams", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
                        return false;

                    if (!reader.Read())
                        return false;

                    return reader.TokenType != JsonToken.EndArray;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
