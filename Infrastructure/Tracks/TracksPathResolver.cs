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
        internal static string TrackLayoutPath(string tracksDir, string infohash)
        {
            infohash = NormalizeInfohash(infohash);
            if (!IsValidInfohash(infohash))
                throw new ArgumentException("Invalid infohash.", nameof(infohash));

            string folder = Path.Combine(tracksDir, infohash.Substring(0, 2), infohash[2].ToString());
            return Path.Combine(folder, $"{infohash.Substring(3)}.json");
        }

        internal static string pathDb(string infohash, bool createfolder = false)
        {
            string path = TrackLayoutPath("Data/tracks", infohash);

            if (createfolder)
                Directory.CreateDirectory(Path.GetDirectoryName(path));

            return path;
        }

        /// <summary>Returns the canonical path if the track file exists; otherwise null.</summary>
        internal static string ResolveTrackJsonPath(string infohash, string tracksDir = "Data/tracks")
        {
            string jsonPath = TrackLayoutPath(tracksDir, infohash);
            return File.Exists(jsonPath) ? jsonPath : null;
        }

        internal static string ResolveTrackPath(string infohash) => ResolveTrackJsonPath(infohash);

        internal static bool IsTrackJsonFile(string filename) =>
            filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

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
