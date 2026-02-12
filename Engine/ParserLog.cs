using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JacRed.Models.Details;

namespace JacRed.Engine
{
    public static class ParserLog
    {
        const string LogDir = "Data/log";
        const int MaxNameLength = 50;
        const int MaxTitleLength = 60;

        /// <summary>
        /// Sanitize tracker name to ensure it's safe for use as a filename
        /// </summary>
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
        /// Extract important database keys from torrent for logging
        /// </summary>
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

        public static void Write(string trackerName, string message)
        {
            if (!AppInit.TrackerLogEnabled(trackerName))
                return;

            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);

                string safeTrackerName = SanitizeTrackerName(trackerName);
                string logPath = Path.Combine(LogDir, $"{safeTrackerName}.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[ParserLog] I/O error while writing tracker log for '{trackerName}': {ex}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
        }

        /// <summary>
        /// Write structured log with key-value pairs for better debugging
        /// </summary>
        public static void Write(string trackerName, string message, Dictionary<string, object> data)
        {
            if (!AppInit.TrackerLogEnabled(trackerName))
                return;

            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);

                string safeTrackerName = SanitizeTrackerName(trackerName);
                string logPath = Path.Combine(LogDir, $"{safeTrackerName}.log");

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
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ParserLog] Unexpected error while writing tracker log for '{trackerName}': {ex}");
            }
        }

        /// <summary>
        /// Write log with statistics (parsed, processed, updated, failed counts)
        /// </summary>
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
        /// Helper method to build log data for torrent operations
        /// </summary>
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
        /// Log when a torrent is added (new entry) with full database keys
        /// </summary>
        public static void WriteAdded(string trackerName, TorrentBaseDetails t)
        {
            if (t == null)
                return;

            var data = BuildTorrentLogData("added", t);
            Write(trackerName, "Torrent added", data);
        }

        /// <summary>
        /// Log when a torrent is updated (existing entry changed) with full database keys
        /// </summary>
        public static void WriteUpdated(string trackerName, TorrentBaseDetails t, string reason = null)
        {
            if (t == null)
                return;

            var data = BuildTorrentLogData("updated", t, reason);
            Write(trackerName, "Torrent updated", data);
        }

        /// <summary>
        /// Log when a torrent is skipped (no changes needed) with full database keys
        /// </summary>
        public static void WriteSkipped(string trackerName, TorrentBaseDetails t, string reason = null)
        {
            if (t == null)
                return;

            var data = BuildTorrentLogData("skipped", t, reason);
            Write(trackerName, "Torrent skipped", data);
        }

        /// <summary>
        /// Log when a torrent operation failed with full database keys
        /// </summary>
        public static void WriteFailed(string trackerName, TorrentBaseDetails t, string reason = null)
        {
            if (t == null)
                return;

            var data = BuildTorrentLogData("failed", t, reason);
            Write(trackerName, "Torrent failed", data);
        }
    }
}
