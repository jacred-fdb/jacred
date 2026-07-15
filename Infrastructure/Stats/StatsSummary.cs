using System.IO;

namespace JacRed.Infrastructure.Stats
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
    }
}
