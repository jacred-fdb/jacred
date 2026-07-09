using JacRed.Infrastructure.Configuration;
using JacRed.Infrastructure.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JacRed.Configuration
{
    public sealed class AppConfigurationProvider : IOptionsMonitor<AppOptions>
    {
        static readonly object InitLock = new object();
        static AppConfigurationProvider _instance;

        readonly object _configLock = new object();
        (AppInit config, string path, DateTime lastWrite) _cache = default;
        readonly List<Action<AppOptions, string>> _changeCallbacks = new List<Action<AppOptions, string>>();

        public static AppConfigurationProvider Instance
        {
            get
            {
                EnsureInitialized();
                return _instance;
            }
        }

        public static void EnsureInitialized()
        {
            if (_instance != null) return;
            lock (InitLock)
            {
                if (_instance != null) return;
                _instance = new AppConfigurationProvider();
                _instance.RefreshIfChanged();
            }
        }

        public AppInit Current
        {
            get { lock (_configLock) { return _cache.config ?? new AppInit(); } }
        }

        AppOptions IOptionsMonitor<AppOptions>.CurrentValue => Current;

        public AppOptions Get(string name) => Current;

        public IDisposable OnChange(Action<AppOptions, string> listener)
        {
            lock (_changeCallbacks)
                _changeCallbacks.Add(listener);
            return new ChangeCallbackRegistration(this, listener);
        }

        /// <summary>Returns current configuration as JSON with sensitive values redacted.</summary>
        public string GetSafeConfigJson()
        {
            var c = Current;
            if (c == null) return "{}";
            var jo = JObject.FromObject(c);
            RedactSensitive(jo);
            return jo.ToString(Formatting.Indented);
        }

        public static bool TrackerLogEnabled(AppOptions config, string trackerName)
        {
            bool parserLogEnabled = config?.logParsers == true;
            if (!parserLogEnabled || string.IsNullOrWhiteSpace(trackerName))
                return false;
            switch (trackerName.ToLowerInvariant())
            {
                case "anidub": return config.Anidub.log;
                case "aniliberty": return config.Aniliberty.log;
                case "animelayer": return config.Animelayer.log;
                case "baibako": return config.Baibako.log;
                case "bitru": return config.Bitru.log;
                case "knaben": return config.Knaben.log;
                case "kinozal": return config.Kinozal.log;
                case "lostfilm": return config.Lostfilm.log;
                case "mazepa": return config.Mazepa.log;
                case "megapeer": return config.Megapeer.log;
                case "nnmclub": return config.NNMClub.log;
                case "rutor": return config.Rutor.log;
                case "rutracker": return config.Rutracker.log;
                case "selezen": return config.Selezen.log;
                case "toloka": return config.Toloka.log;
                case "torrentby": return config.TorrentBy.log;
                default: return parserLogEnabled;
            }
        }

        public void RefreshIfChanged(string forceLogLabel = null)
        {
            try
            {
                string logLabel = forceLogLabel;
                string logPath = null;
                AppInit previous = null;

                lock (_configLock)
                {
                    previous = _cache.config;
                    var (path, lastWrite) = AppConfigurationLoader.GetConfigSource();

                    if (_cache.config == null)
                    {
                        if (path == null)
                        {
                            _cache = (new AppInit(), null, default);
                            if (logLabel == null) logLabel = "config (default)";
                        }
                        else
                        {
                            _cache = (AppConfigurationLoader.LoadFromFile(path), path, lastWrite);
                            if (logLabel == null)
                            {
                                logLabel = "config (start)";
                                logPath = path;
                            }
                        }
                    }
                    else if (path != null && (_cache.path != path || _cache.lastWrite != lastWrite))
                    {
                        bool isReload = _cache.path != null;
                        _cache = (AppConfigurationLoader.LoadFromFile(path), path, lastWrite);
                        if (logLabel == null)
                        {
                            logLabel = isReload ? "config (reload)" : "config (start)";
                            logPath = path;
                        }
                    }
                }

                if (logLabel != null)
                    LogSafeConfig(logLabel, logPath);

                var current = Current;
                JacRedLogSettings.Apply(current);
                if (!ReferenceEquals(previous, current) && previous != null && forceLogLabel == null)
                    NotifyChange(current);
            }
            catch { }
        }

        void ReloadFromDisk(string path)
        {
            lock (_configLock)
            {
                _cache = (AppConfigurationLoader.LoadFromFile(path), path, File.GetLastWriteTimeUtc(path));
            }
            LogSafeConfig("config (saved)", path);
            JacRedLogSettings.Apply(Current);
            NotifyChange(Current);
        }

        void NotifyChange(AppOptions current)
        {
            Action<AppOptions, string>[] callbacks;
            lock (_changeCallbacks)
                callbacks = _changeCallbacks.ToArray();

            foreach (var cb in callbacks)
            {
                try { cb(current, Options.DefaultName); } catch { }
            }
        }

        void LogSafeConfig(string label, string source = null)
        {
            try
            {
                var src = string.IsNullOrEmpty(source) ? "" : $" from {source}";
                JacRedLog.Information(JacRedLogCategories.Config, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {label}{src} applied (sensitive data redacted):");
                JacRedLog.Information(JacRedLogCategories.Config, GetSafeConfigJson());
            }
            catch { }
        }

        static void RedactSensitive(JToken token)
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

        public ConfigSourceInfo GetConfigSourceInfo() => AppConfigurationLoader.GetConfigSourceInfo();

        public JObject GetConfigData(bool redactSensitive = false)
        {
            var c = Current;
            if (c == null) return new JObject();
            var jo = JObject.FromObject(c);
            if (redactSensitive)
                RedactSensitive(jo);
            return jo;
        }

        public string GetConfigContent(bool redactSensitive = false, string format = null)
        {
            var c = Current;
            if (c == null) return format == "json" ? "{}" : "---\n";

            var jo = JObject.FromObject(c);
            if (redactSensitive)
                RedactSensitive(jo);

            return AppConfigurationLoader.RenderConfigObject(jo, format ?? GetConfigSourceInfo().format ?? "yaml");
        }

        public ConfigValidationResult ValidateConfigObject(JObject data)
        {
            var result = new ConfigValidationResult();
            if (data == null)
            {
                result.error = "Данные конфигурации пусты";
                return result;
            }

            try
            {
                var parsed = data.ToObject<AppInit>();
                return ValidateConfigModel(parsed);
            }
            catch (JsonSerializationException ex)
            {
                result.error = ex.Message;
                result.errors.Add(ex.Message);
                return result;
            }
            catch (Exception ex)
            {
                result.error = ex.Message;
                return result;
            }
        }

        public ConfigValidationResult ValidateConfigContent(string content, string format)
        {
            var result = new ConfigValidationResult();
            if (string.IsNullOrWhiteSpace(content))
            {
                result.error = "Конфигурация пуста";
                return result;
            }

            try
            {
                var parsed = AppConfigurationLoader.TryParseConfigContent(content, format, out var error);
                if (parsed == null)
                {
                    result.error = error ?? "Не удалось разобрать конфигурацию";
                    return result;
                }

                return ValidateConfigModel(parsed);
            }
            catch (Exception ex)
            {
                result.error = ex.Message;
            }

            return result;
        }

        ConfigValidationResult ValidateConfigModel(AppInit parsed)
        {
            var result = new ConfigValidationResult();
            ConfigSchema.ValidateAgainstSchema(parsed, result.errors, result.warnings);
            result.ok = result.errors.Count == 0;
            if (!result.ok)
                result.error = result.errors[0];
            return result;
        }

        public List<ConfigDiffEntry> ComputeConfigDiff(JObject proposed, bool redactSensitive = false)
        {
            var current = JObject.FromObject(Current ?? new AppInit());
            var normalizedProposed = NormalizeConfigJObject(proposed);

            if (redactSensitive)
            {
                RedactSensitive(current);
                RedactSensitive(normalizedProposed);
            }

            return ConfigSchema.ComputeDiff(current, normalizedProposed);
        }

        public JObject NormalizeConfigJObject(JObject proposed)
        {
            if (proposed == null) return new JObject();
            try
            {
                var model = proposed.ToObject<AppInit>();
                return model == null ? (JObject)proposed.DeepClone() : JObject.FromObject(model);
            }
            catch (JsonException)
            {
                return (JObject)proposed.DeepClone();
            }
        }

        public (bool ok, string error, JObject data, string content) FormatConfigObject(JObject proposed, string format = null)
        {
            if (proposed == null)
                return (false, "Данные конфигурации пусты", null, null);

            try
            {
                var parsed = proposed.ToObject<AppInit>();
                if (parsed == null)
                    return (false, "Не удалось преобразовать конфигурацию", null, null);

                var validation = ValidateConfigModel(parsed);
                if (!validation.ok)
                    return (false, validation.error ?? "Ошибка валидации", null, null);

                var normalized = JObject.FromObject(parsed);
                var fmt = format ?? GetConfigSourceInfo().format ?? "yaml";
                var content = AppConfigurationLoader.RenderConfigObject(normalized, fmt);
                return (true, null, normalized, content);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null, null);
            }
        }

        public (bool ok, string error, ConfigSourceInfo info) SaveConfigObject(JObject data, string format = null)
        {
            if (data == null)
                return (false, "Данные конфигурации пусты", null);

            try
            {
                var parsed = data.ToObject<AppInit>();
                if (parsed == null)
                    return (false, "Не удалось преобразовать конфигурацию", null);

                var validation = ValidateConfigModel(parsed);
                if (!validation.ok)
                    return (false, validation.error ?? "Ошибка валидации", null);

                var jo = JObject.FromObject(parsed);
                return SaveConfigObjectInternal(jo, format);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }

        public (bool ok, string error, ConfigSourceInfo info) SaveConfigContent(string content, string format = null)
        {
            if (string.IsNullOrWhiteSpace(content))
                return (false, "Конфигурация пуста", null);

            var sourceInfo = GetConfigSourceInfo();
            var outputFormat = format ?? sourceInfo.format ?? "yaml";

            var parsed = AppConfigurationLoader.TryParseConfigContent(content, DetectFormat(content, outputFormat), out var parseError);
            if (parsed == null)
                return (false, parseError ?? "Не удалось разобрать конфигурацию", sourceInfo);

            var validation = ValidateConfigModel(parsed);
            if (!validation.ok)
                return (false, validation.error ?? "Ошибка валидации", sourceInfo);

            var jo = JObject.FromObject(parsed);
            return SaveConfigObjectInternal(jo, format);
        }

        (bool ok, string error, ConfigSourceInfo info) SaveConfigObjectInternal(JObject jo, string format)
        {
            var sourceInfo = GetConfigSourceInfo();
            var outputFormat = format ?? sourceInfo.format ?? "yaml";
            var targetPath = sourceInfo.path ?? (outputFormat == "json" ? AppConfigurationLoader.ConfigFileJson : AppConfigurationLoader.ConfigFileYaml);

            try
            {
                var serialized = AppConfigurationLoader.SerializeConfigObject(jo, outputFormat);
                AppConfigurationLoader.WriteConfigAtomically(targetPath, serialized);
                ReloadFromDisk(targetPath);
                return (true, null, GetConfigSourceInfo());
            }
            catch (Exception ex)
            {
                return (false, ex.Message, sourceInfo);
            }
        }

        static string DetectFormat(string content, string fallback)
            => AppConfigurationLoader.DetectConfigFormat(content, fallback);

        sealed class ChangeCallbackRegistration : IDisposable
        {
            readonly AppConfigurationProvider _provider;
            readonly Action<AppOptions, string> _listener;

            public ChangeCallbackRegistration(AppConfigurationProvider provider, Action<AppOptions, string> listener)
            {
                _provider = provider;
                _listener = listener;
            }

            public void Dispose()
            {
                lock (_provider._changeCallbacks)
                    _provider._changeCallbacks.Remove(_listener);
            }
        }
    }
}
