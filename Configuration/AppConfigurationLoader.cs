using JacRed;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace JacRed.Configuration
{
    public static class AppConfigurationLoader
    {
        public const string ConfigFileYaml = "init.yaml";
        public const string ConfigFileJson = "init.conf";

        /// <summary>Config file priority: init.yaml wins over init.conf. If both exist, init.yaml is used.</summary>
        public static (string path, DateTime lastWrite) GetConfigSource()
        {
            var hasYaml = File.Exists(ConfigFileYaml);
            var hasJson = File.Exists(ConfigFileJson);
            if (hasYaml)
                return (ConfigFileYaml, File.GetLastWriteTimeUtc(ConfigFileYaml));
            if (hasJson)
                return (ConfigFileJson, File.GetLastWriteTimeUtc(ConfigFileJson));
            return (null, default);
        }

        public static ConfigSourceInfo GetConfigSourceInfo()
        {
            var (path, lastWrite) = GetConfigSource();
            return new ConfigSourceInfo
            {
                path = path,
                format = path == null ? null : (path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ? "yaml" : "json"),
                exists = path != null,
                lastModifiedUtc = path == null ? (DateTime?)null : lastWrite
            };
        }

        public static AppInit LoadFromFile(string path)
        {
            var text = File.ReadAllText(path);
            if (path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                var deserializer = new DeserializerBuilder().Build();
                var yamlObj = deserializer.Deserialize<object>(new StringReader(text));
                var json = JsonConvert.SerializeObject(yamlObj);
                return JsonConvert.DeserializeObject<AppInit>(json);
            }
            return JsonConvert.DeserializeObject<AppInit>(text);
        }

        public static AppInit TryParseConfigContent(string content, string format, out string error)
        {
            error = null;
            try
            {
                if (string.Equals(format, "yaml", StringComparison.OrdinalIgnoreCase))
                {
                    var deserializer = new DeserializerBuilder().Build();
                    var yamlObj = deserializer.Deserialize<object>(new StringReader(content));
                    var json = JsonConvert.SerializeObject(yamlObj);
                    return JsonConvert.DeserializeObject<AppInit>(json);
                }

                return JsonConvert.DeserializeObject<AppInit>(content);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static string DetectConfigFormat(string content, string fallback = "yaml")
        {
            if (string.IsNullOrWhiteSpace(content)) return fallback;
            var trimmed = content.TrimStart();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                return "json";
            if (trimmed.StartsWith("---") || trimmed.Contains(':'))
                return "yaml";
            return fallback;
        }

        /// <summary>
        /// MVC uses System.Text.Json; POST body.data arrives as JsonElement, not JObject.
        /// JObject.FromObject(JsonElement) loses values — always parse via raw JSON text.
        /// </summary>
        public static JObject RequestDataToJObject(object dataObj)
        {
            if (dataObj == null) return null;
            if (dataObj is JObject jo) return jo;
            if (dataObj is JsonElement el)
            {
                if (el.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("data must be a JSON object");
                return JObject.Parse(el.GetRawText());
            }
            return JObject.FromObject(dataObj);
        }

        public static (JObject data, string error) TryParseRequestToJObject(string content, string format, object dataObj)
        {
            if (dataObj != null)
            {
                try
                {
                    return (RequestDataToJObject(dataObj), null);
                }
                catch (Exception ex)
                {
                    return (null, ex.Message);
                }
            }

            if (string.IsNullOrWhiteSpace(content))
                return (null, "Укажите data или content");

            var fmt = format ?? DetectConfigFormat(content);
            var parsed = TryParseConfigContent(content, fmt, out var error);
            if (parsed == null)
                return (null, error ?? "Не удалось разобрать конфигурацию");

            return (JObject.FromObject(parsed), null);
        }

        public static string SerializeConfigObject(JObject jo, string format)
        {
            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                return jo.ToString(Formatting.Indented);

            var plain = JTokenToPlain(jo);
            var serializer = new SerializerBuilder()
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .DisableAliases()
                .Build();
            using var writer = new StringWriter();
            writer.WriteLine("---");
            serializer.Serialize(writer, plain);
            return writer.ToString();
        }

        public static string RenderConfigObject(JObject data, string format = null)
        {
            if (data == null) return string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) ? "{}" : "---\n";
            return SerializeConfigObject(data, format ?? "yaml");
        }

        public static void WriteConfigAtomically(string path, string content)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        static object JTokenToPlain(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            switch (token.Type)
            {
                case JTokenType.Object:
                    return token.Children<JProperty>()
                        .ToDictionary(p => p.Name, p => JTokenToPlain(p.Value));
                case JTokenType.Array:
                    return token.Select(JTokenToPlain).ToList();
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.Float:
                    return token.Value<double>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.String:
                    return token.Value<string>();
                default:
                    return ((JValue)token).Value;
            }
        }
    }
}
