namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Torznab / Jackett compatibility layer (jacred-proxy feature parity).
    /// </summary>
    public class TorznabSettings
    {
        /// <summary>Enable native Torznab XML at /torznab/api and /api/v2.0/indexers/{id}/results/torznab/api.</summary>
        public bool enable { get; set; } = true;

        /// <summary>v1 merge: true | false | auto (card=minimal, fuzzy=capped).</summary>
        public string mergeV1 { get; set; } = "auto";

        /// <summary>Max v1 search pairs when mergeV1=auto in fuzzy mode.</summary>
        public int maxV1Pairs { get; set; } = 4;

        /// <summary>v1 sort for IMDB/KP searches (default seeders desc).</summary>
        public string v1Sort { get; set; } = "sid";

        /// <summary>Strip trailing year from fuzzy query and search both variants.</summary>
        public bool stripTrailingYear { get; set; } = true;

        /// <summary>Add voice tags to Torznab titles.</summary>
        public bool enrichTitles { get; set; } = true;

        /// <summary>Skip client-side Torznab category post-filter.</summary>
        public bool skipCatFilter { get; set; } = true;
    }
}
