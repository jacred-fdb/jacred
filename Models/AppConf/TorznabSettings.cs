namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Torznab XML endpoints only (Sonarr/Radarr/Prowlarr). Search tuning — <see cref="SearchSettings"/>.
    /// </summary>
    public class TorznabSettings
    {
        /// <summary>Enable Torznab XML at /torznab/api and Prowlarr/Jackett Torznab aliases.</summary>
        public bool enable { get; set; } = true;

        /// <summary>Add voice tags to Torznab XML titles.</summary>
        public bool enrichTitles { get; set; } = true;
    }
}
