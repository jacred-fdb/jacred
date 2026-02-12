using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using JacRed.Models.Details;

namespace JacRed.Engine
{
    public static class ParserLog
    {
        const string LogDir = "Data/log";
        const int MaxNameLength = 50;
        const int MaxTitleLength = 60;

        /// <summary>
        /// Sanitizes the tracker name to ensure it's safe for use as a filename.
        /// </summary>
        /// <param name="trackerName">The tracker name to sanitize.</param>
        /// <returns>A sanitized tracker name safe for use as a filename, or "unknown" if the input is invalid or empty.</returns>
        static string SanitizeTrackerName(string trackerName)
        {
            if (string.IsNullOrWhiteSpace(trackerName))
                return "unknown";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = trackerName;
            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            sanitized = sanitized.Replace('/', '_').Replace('\\', '_');
            sanitized = sanitized.TrimStart('.', '/', '\\');

            if (string.IsNullOrWhiteSpace(sanitized))
                return "unknown";

            if (Path.IsPathRooted(sanitized))
            {
                sanitized = Path.GetFileName(sanitized);
                if (string.IsNullOrWhiteSpace(sanitized))
                    return "unknown";
            }

            return sanitized;
        }

        /// <summary>
        /// Extracts important database keys from a torrent for logging purposes.
        /// </summary>
        /// <param name="t">The torrent details to extract keys from.</param>
        /// <returns>A dictionary containing the extracted torrent keys and their values.</returns>
        static Dictionary<string, object> ExtractTorrentKeys(TorrentBaseDetails t)
        {
            var data = new Dictionary<string, object>();

            if (!string.IsNullOrWhiteSpace(t.name))
                data["name"] = t.name.Length > MaxNameLength ? t.name.Substring(0, MaxNameLength) + "..." : t.name;

            if (!string.IsNullOrWhiteSpace(t.originalname))
                data["originalname"] = t.originalname.Length > MaxNameLength ? t.originalname.Substring(0, MaxNameLength) + "..." : t.originalname;

            if (!string.IsNullOrWhiteSpace(t._sn))
                data["_sn"] = t._sn;

            if (!string.IsNullOrWhiteSpace(t._so))
                data["_so"] = t._so;

            if (!string.IsNullOrWhiteSpace(t.magnet))
            {
                // Extract the full hash from magnet link
                var hashMatch = System.Text.RegularExpressions.Regex.Match(t.magnet, "btih:([a-fA-F0-9]{40})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                data["magnet"] = hashMatch.Success ? hashMatch.Groups[1].Value : "yes";
            }

            if (t.createTime != default)
                data["createTime"] = t.createTime.ToString("yyyy-MM-dd");

            if (t.updateTime != default)
                data["updateTime"] = t.updateTime.ToString("yyyy-MM-dd HH:mm:ss");

            if (!string.IsNullOrWhiteSpace(t.sizeName))
                data["size"] = t.sizeName;

            if (t.types != null && t.types.Length > 0)
                data["types"] = string.Join(",", t.types);

            return data;
        }

        /// <summary>
        /// Writes a log entry with the specified message to the tracker's log file.
        /// </summary>
        /// <param name="trackerName">The name of the tracker to log for.</param>
        /// <param name="message">The message to write to the log.</param>
        public static void Write(string trackerName, string message)
        {
            if (!AppInit.TrackerLogEnabled(trackerName))
                return;

            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);

                string safeTrackerName = SanitizeTrackerName(trackerName);
                string rawName = string.IsNullOrWhiteSpace(safeTrackerName) ? "tracker" : safeTrackerName;
                string fileName = Path.GetFileName(rawName) + ".log";
                if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    fileName = "unknown.log";
                // Ensure fileName is not rooted (absolute path) to prevent Path.Combine from ignoring logDirSafe
                if (Path.IsPathRooted(fileName))
                    fileName = "unknown.log";
                // Normalize to file name only to remove any directory separators
                fileName = Path.GetFileName(fileName);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "unknown.log";
                string logDirSafe = string.IsNullOrWhiteSpace(LogDir) ? Directory.GetCurrentDirectory() : LogDir;
                if (logDirSafe.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    logDirSafe = Directory.GetCurrentDirectory();
                string logPath = Path.Combine(logDirSafe, fileName);

                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[ParserLog] I/O error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (SecurityException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (NotSupportedException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (ArgumentNullException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
        }

        /// <summary>
        /// Writes a structured log entry with key-value pairs for better debugging.
        /// </summary>
        /// <param name="trackerName">The name of the tracker to log for.</param>
        /// <param name="message">The message to write to the log.</param>
        /// <param name="data">A dictionary containing key-value pairs to include in the log entry.</param>
        public static void Write(string trackerName, string message, Dictionary<string, object> data)
        {
            if (!AppInit.TrackerLogEnabled(trackerName))
                return;

            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);

                string safeTrackerName = SanitizeTrackerName(trackerName);
                string rawName = string.IsNullOrWhiteSpace(safeTrackerName) ? "tracker" : safeTrackerName;
                string fileName = Path.GetFileName(rawName) + ".log";
                if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    fileName = "unknown.log";
                // Ensure fileName is not rooted (absolute path) to prevent Path.Combine from ignoring logDirSafe
                if (Path.IsPathRooted(fileName))
                    fileName = "unknown.log";
                // Normalize to file name only to remove any directory separators
                fileName = Path.GetFileName(fileName);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "unknown.log";
                string logDirSafe = string.IsNullOrWhiteSpace(LogDir) ? Directory.GetCurrentDirectory() : LogDir;
                if (logDirSafe.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    logDirSafe = Directory.GetCurrentDirectory();
                string logPath = Path.Combine(logDirSafe, fileName);

                var parts = new List<string> { message };
                if (data != null && data.Count > 0)
                {
                    var kvPairs = data.Select(kv => $"{kv.Key}={kv.Value}");
                    parts.Add($" | {string.Join(", ", kvPairs)}");
                }

                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {string.Join("", parts)}\n");
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[ParserLog] I/O error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (SecurityException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (NotSupportedException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (ArgumentNullException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
        }

        /// <summary>
        /// Writes a log entry with statistics including parsed, processed, updated, and failed counts.
        /// </summary>
        /// <param name="trackerName">The name of the tracker to log for.</param>
        /// <param name="message">The message to write to the log.</param>
        /// <param name="parsed">The number of items parsed. Only included if greater than 0.</param>
        /// <param name="processed">The number of items processed. Only included if greater than 0.</param>
        /// <param name="updated">The number of items updated. Only included if greater than 0.</param>
        /// <param name="failed">The number of items that failed. Only included if greater than 0.</param>
        public static void WriteStats(string trackerName, string message, int parsed = 0, int processed = 0, int updated = 0, int failed = 0)
        {
            var data = new Dictionary<string, object>();
            if (parsed > 0) data["parsed"] = parsed;
            if (processed > 0) data["processed"] = processed;
            if (updated > 0) data["updated"] = updated;
            if (failed > 0) data["failed"] = failed;

            Write(trackerName, message, data);
        }

        /// <summary>
        /// Builds log data dictionary for torrent operations.
        /// </summary>
        /// <param name="action">The action being performed (e.g., "added", "updated", "skipped", "failed").</param>
        /// <param name="t">The torrent details to include in the log data.</param>
        /// <param name="reason">An optional reason for the action.</param>
        /// <returns>A dictionary containing the log data with action, torrent keys, and optional reason.</returns>
        private static Dictionary<string, object> BuildTorrentLogData(string action, TorrentBaseDetails t, string reason = null)
        {
            var data = new Dictionary<string, object> { { "action", action } };

            if (t != null && !string.IsNullOrWhiteSpace(t.url))
                data["url"] = t.url;

            if (t != null && !string.IsNullOrWhiteSpace(t.title))
                data["title"] = t.title.Length > MaxTitleLength ? t.title.Substring(0, MaxTitleLength) + "..." : t.title;

            if (!string.IsNullOrWhiteSpace(reason))
                data["reason"] = reason;

            // Merge important database keys
            if (t != null)
            {
                var keys = ExtractTorrentKeys(t);
                foreach (var kv in keys)
                    data[kv.Key] = kv.Value;
            }

            return data;
        }

        /// <summary>
        /// Logs when a torrent is added as a new entry with full database keys.
        /// </summary>
        /// <param name="trackerName">The name of the tracker to log for.</param>
        /// <param name="t">The torrent details that were added.</param>
        public static void WriteAdded(string trackerName, TorrentBaseDetails t)
        {
            if (t == null)
                return;

            var data = BuildTorrentLogData("added", t);
            Write(trackerName, "Torrent added", data);
        }

        /// <summary>
        /// Logs when a torrent is updated (existing entry changed) with full database keys.
        /// </summary>
        /// <param name="trackerName">The name of the tracker to log for.</param>
        /// <param name="t">The torrent details that were updated.</param>
        /// <param name="reason">An optional reason for the update.</param>
        public static void WriteUpdated(string trackerName, TorrentBaseDetails t, string reason = null)
        {
            if (t == null)
                return;

            var data = BuildTorrentLogData("updated", t, reason);
            Write(trackerName, "Torrent updated", data);
        }

        /// <summary>
        /// Logs when a torrent is skipped (no changes needed) with full database keys.
        /// </summary>
        /// <param name="trackerName">The name of the tracker to log for.</param>
        /// <param name="t">The torrent details that were skipped.</param>
        /// <param name="reason">An optional reason for skipping the torrent.</param>
        public static void WriteSkipped(string trackerName, TorrentBaseDetails t, string reason = null)
        {
            if (t == null)
                return;

            var data = BuildTorrentLogData("skipped", t, reason);
            Write(trackerName, "Torrent skipped", data);
        }

        /// <summary>
        /// Logs when a torrent operation failed with full database keys.
        /// </summary>
        /// <param name="trackerName">The name of the tracker to log for.</param>
        /// <param name="t">The torrent details for which the operation failed.</param>
        /// <param name="reason">An optional reason describing why the operation failed.</param>
        public static void WriteFailed(string trackerName, TorrentBaseDetails t, string reason = null)
        {
            if (t == null)
                return;

            var data = BuildTorrentLogData("failed", t, reason);
            Write(trackerName, "Torrent failed", data);
        }
    }
}
