using System.Collections.Generic;
using Newtonsoft.Json;

namespace JacRed.Models.tParse
{
    /// <summary>Request body for Knaben API v1 (POST JSON).</summary>
    public class KnabenApiRequest
    {
        [JsonProperty("query")]
        public string Query { get; set; }

        [JsonProperty("search_field")]
        public string SearchField { get; set; }

        [JsonProperty("search_type")]
        public string SearchType { get; set; }

        [JsonProperty("categories")]
        public int[] Categories { get; set; }

        [JsonProperty("order_by")]
        public string OrderBy { get; set; }

        [JsonProperty("order_direction")]
        public string OrderDirection { get; set; }

        [JsonProperty("from")]
        public int From { get; set; }

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("hide_unsafe")]
        public bool HideUnsafe { get; set; }

        [JsonProperty("hide_xxx")]
        public bool HideXxx { get; set; }

        [JsonProperty("seconds_since_last_seen")]
        public int? SecondsSinceLastSeen { get; set; }
    }

    /// <summary>Response from Knaben API v1.</summary>
    public class KnabenApiResponse
    {
        [JsonProperty("total")]
        public KnabenTotal Total { get; set; }

        [JsonProperty("hits")]
        public List<KnabenHit> Hits { get; set; }
    }

    public class KnabenTotal
    {
        [JsonProperty("relation")]
        public string Relation { get; set; }

        [JsonProperty("value")]
        public int Value { get; set; }
    }

    /// <summary>Single hit from Knaben API.</summary>
    public class KnabenHit
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("bytes")]
        public long Bytes { get; set; }

        [JsonProperty("seeders")]
        public int Seeders { get; set; }

        [JsonProperty("peers")]
        public int Peers { get; set; }

        [JsonProperty("magnetUrl")]
        public string MagnetUrl { get; set; }

        [JsonProperty("link")]
        public string Link { get; set; }

        [JsonProperty("details")]
        public string Details { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("categoryId")]
        public int[] CategoryId { get; set; }

        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("lastSeen")]
        public string LastSeen { get; set; }

        [JsonProperty("tracker")]
        public string Tracker { get; set; }

        [JsonProperty("trackerId")]
        public string TrackerId { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }
    }
}
