namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Combined indexer search: Jackett JSON and Torznab XML (shared pipeline).
    /// </summary>
    public class SearchSettings
    {
        /// <summary>v1 fuzzy merge: false | auto (fuzzy only) | true (always).</summary>
        public string mergeV1 { get; set; } = "auto";

        /// <summary>Max v1 search pairs when mergeV1=auto in fuzzy mode.</summary>
        public int maxV1Pairs { get; set; } = 4;

        /// <summary>v1 sort (sid = seeders desc, pir, size). Also used for IMDB/KP exact v1.</summary>
        public string v1Sort { get; set; } = "sid";

        /// <summary>Strip trailing year from fuzzy query and search both variants.</summary>
        public bool stripTrailingYear { get; set; } = true;

        /// <summary>Skip category post-filter on server (client filters by cat/Category[]).</summary>
        public bool skipCatFilter { get; set; } = true;
    }
}
