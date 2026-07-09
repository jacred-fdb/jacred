using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;

namespace JacRed.Infrastructure.OpenApi
{
    public static class OpenApiSpecHelper
    {
        public static string GetYamlPath(string contentRoot)
        {
            foreach (var root in ResolveContentRoots(contentRoot))
            {
                var path = Path.Combine(root, "wwwroot", "openapi.yaml");
                if (File.Exists(path))
                    return path;
            }

            return Path.Combine(contentRoot ?? ".", "wwwroot", "openapi.yaml");
        }

        static IEnumerable<string> ResolveContentRoots(string contentRoot)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = new List<string>();
            foreach (var dir in new[]
            {
                contentRoot,
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory
            })
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                if (!TryGetFullPath(dir, out var full)) continue;
                if (seen.Add(full))
                    roots.Add(full);
            }
            return roots;
        }

        static bool TryGetFullPath(string dir, out string full)
        {
            full = null;
            try
            {
                full = Path.GetFullPath(dir);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetOpenApiJson(string contentRoot, out string json, out string error)
        {
            json = null;
            error = null;
            var path = GetYamlPath(contentRoot);
            if (!File.Exists(path))
            {
                error = $"OpenAPI file not found: {path}";
                return false;
            }

            try
            {
                var yaml = File.ReadAllText(path);
                var deserializer = new DeserializerBuilder().Build();
                var yamlObj = deserializer.Deserialize<object>(new StringReader(yaml));
                var token = JToken.FromObject(yamlObj ?? new object());
                json = token.ToString(Formatting.None);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
