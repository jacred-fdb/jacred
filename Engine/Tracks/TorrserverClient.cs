using JacRed.Models.Tracks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace JacRed.Engine
{
    /// <summary>
    /// Client for Torrserver API: add/get/rem torrents, GET /ffp for ffprobe.
    /// API: POST /torrents (action add|get|rem), GET /ffp/{hash}/{id}. See temp/TorrServer/server/web/api/ (torrents.go, ffprobe.go).
    /// Request JSON must use lowercase keys: action, link, hash, save_to_db. Supports optional Basic auth per server.
    /// </summary>
    public static class TorrserverClient
    {
        /// <summary>
        /// Build optional Basic auth header from per-server credentials. Returns null if both username and password are empty.
        /// </summary>
        public static List<(string name, string val)> AuthHeaders(string username = null, string password = null)
        {
            string user = username ?? "";
            string pass = password ?? "";
            if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass)) return null;
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
            return new List<(string, string)> { ("Authorization", "Basic " + encoded) };
        }

        /// <summary>
        /// POST /torrents { "action": "add", "link": magnet, "save_to_db": false }. Torrserver will not persist torrent to its DB.
        /// </summary>
        public static async Task<string> AddTorrent(string baseUrl, string magnet, int timeoutSeconds = 120, string username = null, string password = null)
        {
            string url = baseUrl.TrimEnd('/') + "/torrents";
            var body = new { action = "add", link = magnet, save_to_db = false };
            string json = JsonConvert.SerializeObject(body);
            return await CORE.HttpClient.Post(url, new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json"),
                timeoutSeconds: timeoutSeconds, addHeaders: AuthHeaders(username, password));
        }

        /// <summary>
        /// POST /torrents { "action": "get", "hash": hash }. Returns status or null if 404/error.
        /// </summary>
        public static async Task<TorrserverTorrentStatus> GetTorrent(string baseUrl, string hash, int timeoutSeconds = 30, string username = null, string password = null)
        {
            string url = baseUrl.TrimEnd('/') + "/torrents";
            var body = new { action = "get", hash = hash };
            string json = JsonConvert.SerializeObject(body);
            var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
            var result = await CORE.HttpClient.Post<TorrserverTorrentStatus>(url, content,
                timeoutSeconds: timeoutSeconds, addHeaders: AuthHeaders(username, password));
            return result;
        }

        /// <summary>
        /// POST /torrents { "action": "rem", "hash": hash }. Best-effort; no throw.
        /// </summary>
        public static async Task RemTorrent(string baseUrl, string hash, int timeoutSeconds = 15, string username = null, string password = null)
        {
            try
            {
                string url = baseUrl.TrimEnd('/') + "/torrents";
                var body = new { action = "rem", hash = hash };
                string json = JsonConvert.SerializeObject(body);
                await CORE.HttpClient.Post(url, new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json"),
                    timeoutSeconds: timeoutSeconds, addHeaders: AuthHeaders(username, password));
            }
            catch (Exception) { /* best effort */ }
        }

        /// <summary>
        /// GET /ffp/{hash}/{fileIndex}. Returns ffprobe JSON deserialized or null.
        /// </summary>
        public static async Task<FfprobeModel> Ffp(string baseUrl, string hash, int fileIndex = 1, int timeoutSeconds = 180, string username = null, string password = null)
        {
            string url = $"{baseUrl.TrimEnd('/')}/ffp/{hash}/{fileIndex}";
            return await CORE.HttpClient.Get<FfprobeModel>(url, timeoutSeconds: timeoutSeconds, addHeaders: AuthHeaders(username, password));
        }

        /// <summary>
        /// Check if torrent has metadata (file_stats present and non-empty).
        /// </summary>
        public static bool HasMetadata(TorrserverTorrentStatus status)
        {
            return status?.FileStats != null && status.FileStats.Count > 0;
        }
    }
}
