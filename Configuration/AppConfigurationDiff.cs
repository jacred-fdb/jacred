using JacRed.Configuration.Schema;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace JacRed.Configuration
{
    public static class AppConfigurationDiff
    {
        public static void RedactSensitive(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties().ToList())
                {
                    if (ConfigSchema.SensitiveFieldNames.Contains(prop.Name) && prop.Value != null && prop.Value.Type != JTokenType.Null && prop.Value.Type != JTokenType.Undefined)
                    {
                        var val = prop.Value.ToString();
                        if (!string.IsNullOrEmpty(val))
                            prop.Value = "***";
                    }
                    else
                        RedactSensitive(prop.Value);
                }
            }
            else if (token is JArray arr)
            {
                foreach (var item in arr)
                    RedactSensitive(item);
            }
        }

        public static List<ConfigDiffEntry> ComputeConfigDiff(JObject current, JObject proposed, bool redactSensitive = false)
        {
            var normalizedProposed = AppConfigurationValidator.NormalizeConfigJObject(proposed);

            if (redactSensitive)
            {
                RedactSensitive(current);
                RedactSensitive(normalizedProposed);
            }

            return ConfigSchema.ComputeDiff(current, normalizedProposed);
        }
    }
}
