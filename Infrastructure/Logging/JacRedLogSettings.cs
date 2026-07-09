using JacRed.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace JacRed.Infrastructure.Logging
{
    public static class JacRedLogSettings
    {
        public static bool ConsoleTimestamp { get; private set; }
        public static bool TracksConsoleDetail { get; private set; } = true;
        public static int CronSkipFastMs { get; private set; }

        static LogLevel _defaultLevel = LogLevel.Information;
        static Dictionary<string, LogLevel> _categoryLevels;

        public static void Apply(AppOptions conf)
        {
            var o = conf?.logging;
            if (o == null)
            {
                ConsoleTimestamp = false;
                TracksConsoleDetail = false;
                CronSkipFastMs = 100;
                _defaultLevel = LogLevel.Information;
                _categoryLevels = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase)
                {
                    [NormalizeCategoryKey("parsers")] = LogLevel.None
                };
                return;
            }

            ConsoleTimestamp = o.consoleTimestamp;
            TracksConsoleDetail = o.tracksConsoleDetail;
            CronSkipFastMs = o.cronSkipFastMs;
            _defaultLevel = ParseLevel(o.defaultLevel, LogLevel.Information);
            _categoryLevels = BuildCategoryMap(o.categories);
        }

        public static bool IsEnabled(string category, LogLevel level)
        {
            if (_categoryLevels != null && _categoryLevels.TryGetValue(NormalizeCategoryKey(category), out var min))
                return level >= min;
            return level >= _defaultLevel;
        }

        static Dictionary<string, LogLevel> BuildCategoryMap(Dictionary<string, string> categories)
        {
            if (categories == null || categories.Count == 0) return null;
            var map = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in categories)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                if (string.Equals(kv.Value, "None", StringComparison.OrdinalIgnoreCase))
                {
                    map[NormalizeCategoryKey(kv.Key)] = LogLevel.None;
                    continue;
                }
                map[NormalizeCategoryKey(kv.Key)] = ParseLevel(kv.Value, LogLevel.Information);
            }
            return map;
        }

        static string NormalizeCategoryKey(string key)
        {
            if (string.Equals(key, "syncSpidr", StringComparison.OrdinalIgnoreCase))
                return JacRedLogCategories.SyncSpidr;
            if (string.Equals(key, "parsers", StringComparison.OrdinalIgnoreCase))
                return JacRedLogCategories.Parser;
            return key.Trim().ToLowerInvariant();
        }

        public static LogLevel ParseLevel(string value, LogLevel fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return Enum.TryParse<LogLevel>(value, true, out var level) ? level : fallback;
        }
    }
}
