using System.Collections.Generic;
using Newtonsoft.Json;

namespace JacRed.Models.tParse
{
    /// <summary>Response from /api/v1/anime/torrents</summary>
    public class AnilibertyApiResponse
    {
        [JsonProperty("data")]
        public List<AnilibertyTorrent> Data { get; set; }

        [JsonProperty("meta")]
        public AnilibertyMeta Meta { get; set; }
    }

    public class AnilibertyMeta
    {
        [JsonProperty("current_page")]
        public int CurrentPage { get; set; }

        [JsonProperty("last_page")]
        public int LastPage { get; set; }

        [JsonProperty("per_page")]
        public int PerPage { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }

    public class AnilibertyTorrent
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("magnet")]
        public string Magnet { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public string UpdatedAt { get; set; }

        [JsonProperty("quality")]
        public AnilibertyQuality Quality { get; set; }

        [JsonProperty("type")]
        public AnilibertyType Type { get; set; }

        [JsonProperty("codec")]
        public AnilibertyCodec Codec { get; set; }

        [JsonProperty("seeders")]
        public int Seeders { get; set; }

        [JsonProperty("leechers")]
        public int Leechers { get; set; }

        [JsonProperty("bitrate")]
        public int? Bitrate { get; set; }

        [JsonProperty("is_hardsub")]
        public bool IsHardsub { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("completed_times")]
        public int CompletedTimes { get; set; }

        [JsonProperty("color")]
        public AnilibertyColor Color { get; set; }

        [JsonProperty("release")]
        public AnilibertyRelease Release { get; set; }
    }

    public class AnilibertyColor
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public class AnilibertyQuality
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public class AnilibertyType
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public class AnilibertyCodec
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public class AnilibertyRelease
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public AnilibertyReleaseName Name { get; set; }

        [JsonProperty("year")]
        public int? Year { get; set; }

        [JsonProperty("type")]
        public AnilibertyReleaseType Type { get; set; }
    }

    public class AnilibertyReleaseName
    {
        [JsonProperty("main")]
        public string Main { get; set; }

        [JsonProperty("english")]
        public string English { get; set; }

        [JsonProperty("alternative")]
        public string Alternative { get; set; }
    }

    public class AnilibertyReleaseType
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }
}
