using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

namespace JacRed.Infrastructure
{
    /// <summary>Read-only access to Data/temp/stats.json (per-tracker counters).</summary>
    public static class StatsSummary
    {
        public static string ReadAllJson()
        {
            if (!File.Exists(StatsCollector.StatsPath))
                return "[]";
            try
            {
                return File.ReadAllText(StatsCollector.StatsPath);
            }
            catch
            {
                return "[]";
            }
        }

        public static JObject TryFindTracker(string trackerName)
        {
            if (string.IsNullOrWhiteSpace(trackerName) || !File.Exists(StatsCollector.StatsPath))
                return null;

            try
            {
                var arr = JArray.Parse(File.ReadAllText(StatsCollector.StatsPath));
                return arr.Children<JObject>()
                    .FirstOrDefault(o => string.Equals(o["trackerName"]?.ToString(), trackerName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }
    }
}
