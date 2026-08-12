namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Alloha TV API v2 — resolve KP/IMDB/TMDB IDs to titles for FileDB search.
    /// </summary>
    public class AllohaSettings
    {
        public bool enable { get; set; } = true;

        public string baseUrl { get; set; } = "https://apbugall.org";

        /// <summary>Bearer token for Authorization header.</summary>
        public string token { get; set; } = "04941a9a3ca3ac16e2b4327347bbc1";

        public int timeoutSeconds { get; set; } = 8;

        public int cacheHours { get; set; } = 24;

        /// <summary>When client year is absent, filter FileDB hits by Alloha year (±1).</summary>
        public bool filterByYear { get; set; } = true;
    }
}
