namespace JacRed.Models.AppConf
{
    /// <summary>
    /// One Torrserver instance: URL and optional Basic auth.
    /// Used in tservers[] for per-server configuration (with or without auth).
    /// </summary>
    public class TorrserverEntry
    {
        /// <summary>Base URL, e.g. http://127.0.0.1:8090 or https://ts.example.com</summary>
        public string url { get; set; }

        /// <summary>Optional Basic auth username for this server. Omit or leave empty for no auth.</summary>
        public string username { get; set; }

        /// <summary>Optional Basic auth password for this server.</summary>
        public string password { get; set; }
    }
}
