using Newtonsoft.Json.Linq;

namespace JacRed.Models
{
    /// <summary>Request body for config validate, diff, and save.</summary>
    public class ConfigSaveRequest
    {
        /// <summary>Raw init.yaml / init.conf text.</summary>
        public string content { get; set; }

        /// <summary>yaml or json — format hint for content or output file.</summary>
        public string format { get; set; }

        /// <summary>Parsed configuration object (settings form UI).</summary>
        public JObject data { get; set; }
    }
}
