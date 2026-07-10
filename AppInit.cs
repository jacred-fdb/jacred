using JacRed.Configuration;
using JacRed.Configuration.Schema;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace JacRed
{
    /// <summary>Slim facade over <see cref="AppConfigurationProvider"/> — preserves AppInit.conf and Config API entry points.</summary>
    public class AppInit : AppOptions
    {
        static AppConfigurationProvider Provider => AppConfigurationProvider.Instance;

        static AppInit()
        {
            AppConfigurationProvider.EnsureInitialized();
        }

        public static AppInit conf => Provider.Current;

        public static string GetSafeConfigJson() => Provider.GetSafeConfigJson();

        public static bool TrackerLogEnabled(string trackerName)
            => AppConfigurationProvider.TrackerLogEnabled(conf, trackerName);

        public static class TracksIntervalStatic
        {
            public static int task0 => conf?.TracksInterval?.task0 ?? 180;
            public static int task1 => conf?.TracksInterval?.task1 ?? 60;
        }

        #region ConfigManagement facade
        public static ConfigSourceInfo GetConfigSourceInfo() => Provider.GetConfigSourceInfo();

        public static JObject GetConfigData(bool redactSensitive = false) => Provider.GetConfigData(redactSensitive);

        public static string GetConfigContent(bool redactSensitive = false, string format = null)
            => Provider.GetConfigContent(redactSensitive, format);

        public static string RenderConfigObject(JObject data, string format = null)
            => AppConfigurationLoader.RenderConfigObject(data, format);

        public static JObject RequestDataToJObject(object dataObj)
            => AppConfigurationLoader.RequestDataToJObject(dataObj);

        public static (JObject data, string error) TryParseRequestToJObject(string content, string format, object dataObj)
            => AppConfigurationLoader.TryParseRequestToJObject(content, format, dataObj);

        public static ConfigValidationResult ValidateConfigObject(JObject data) => Provider.ValidateConfigObject(data);

        public static ConfigValidationResult ValidateConfigContent(string content, string format)
            => Provider.ValidateConfigContent(content, format);

        public static List<ConfigDiffEntry> ComputeConfigDiff(JObject proposed, bool redactSensitive = false)
            => Provider.ComputeConfigDiff(proposed, redactSensitive);

        public static JObject NormalizeConfigJObject(JObject proposed) => Provider.NormalizeConfigJObject(proposed);

        public static (bool ok, string error, JObject data, string content) FormatConfigObject(JObject proposed, string format = null)
            => Provider.FormatConfigObject(proposed, format);

        public static (bool ok, string error, ConfigSourceInfo info) SaveConfigObject(JObject data, string format = null)
            => Provider.SaveConfigObject(data, format);

        public static (bool ok, string error, ConfigSourceInfo info) SaveConfigContent(string content, string format = null)
            => Provider.SaveConfigContent(content, format);

        public static string DetectConfigFormat(string content, string fallback = "yaml")
            => AppConfigurationLoader.DetectConfigFormat(content, fallback);
        #endregion
    }
}
