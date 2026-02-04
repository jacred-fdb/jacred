using System.Collections.Generic;
using Newtonsoft.Json;

namespace JacRed.Models.tParse
{
    /// <summary>Ответ api.php?get=torrents</summary>
    public class BitruApiResponse
    {
        [JsonProperty("error")]
        public bool Error { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("result")]
        public BitruApiResult Result { get; set; }
    }

    public class BitruApiResult
    {
        /// <summary>Unix timestamp (в ответе может быть string или number) — сохранять для следующего запроса "после этой даты"</summary>
        [JsonProperty("after_date")]
        public object AfterDate { get; set; }

        [JsonProperty("before_date")]
        public object BeforeDate { get; set; }

        [JsonProperty("items")]
        public List<BitruApiItemWrapper> Items { get; set; }
    }

    public class BitruApiItemWrapper
    {
        [JsonProperty("item")]
        public BitruApiItemInner Item { get; set; }
    }

    public class BitruApiItemInner
    {
        [JsonProperty("torrent")]
        public BitruApiTorrent Torrent { get; set; }

        [JsonProperty("info")]
        public BitruApiInfo Info { get; set; }

        [JsonProperty("template")]
        public BitruApiTemplate Template { get; set; }
    }

    public class BitruApiTorrent
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("added")]
        public long Added { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("leechers")]
        public int Leechers { get; set; }

        [JsonProperty("seeders")]
        public int Seeders { get; set; }

        [JsonProperty("file")]
        public string File { get; set; }
    }

    public class BitruApiInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("year")]
        public int Year { get; set; }

        [JsonProperty("country")]
        public List<string> Country { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public class BitruApiTemplate
    {
        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("section")]
        public string Section { get; set; }

        [JsonProperty("subsection")]
        public List<string> Subsection { get; set; }

        [JsonProperty("orig_name")]
        public string OrigName { get; set; }

        [JsonProperty("video")]
        public BitruApiVideo Video { get; set; }

        [JsonProperty("other")]
        public string Other { get; set; }
    }

    public class BitruApiVideo
    {
        [JsonProperty("quality")]
        public string Quality { get; set; }
    }
}
