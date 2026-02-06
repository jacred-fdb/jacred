using System.Collections.Generic;
using Newtonsoft.Json;

namespace JacRed.Models.Tracks
{
    /// <summary>
    /// Torrserver API: POST /torrents { "action": "get" } response.
    /// </summary>
    public class TorrserverTorrentStatus
    {
        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("stat")]
        public int Stat { get; set; }

        [JsonProperty("stat_string")]
        public string StatString { get; set; }

        [JsonProperty("file_stats")]
        public List<TorrserverFileStat> FileStats { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class TorrserverFileStat
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("length")]
        public long Length { get; set; }
    }
}
