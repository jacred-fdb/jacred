using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using JacRed;
using JacRed.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JacRed.Controllers
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

    /// <summary>
    /// Configuration management API (init.yaml / init.conf).
    /// LAN, same-host proxy, or X-Dev-Key when devkey is configured.
    /// Uses Newtonsoft.Json end-to-end (System.Text.Json breaks JObject → [] on the client).
    /// </summary>
    [Route("/api/v1.0/config")]
    public class ConfigController : Controller
    {
        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Include
        };

        static IActionResult ConfigJson(object payload)
            => new ContentResult
            {
                Content = JsonConvert.SerializeObject(payload, JsonSettings),
                ContentType = "application/json; charset=utf-8",
                StatusCode = 200
            };

        static ConfigSaveRequest ParseRequestBody(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var root = JToken.Parse(json);
            if (root.Type != JTokenType.Object) return null;
            var obj = (JObject)root;
            return new ConfigSaveRequest
            {
                content = obj["content"]?.Type == JTokenType.String ? obj["content"].ToString() : null,
                format = obj["format"]?.ToString(),
                data = obj["data"] as JObject
            };
        }

        async Task<ConfigSaveRequest> ReadBodyAsync()
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            return ParseRequestBody(await reader.ReadToEndAsync());
        }

        /// <summary>Form field schema for /settings UI (groups, types, validation hints).</summary>
        [HttpGet("schema")]
        [Produces("application/json")]
        public IActionResult Schema()
            => ConfigJson(new { ok = true, schema = ConfigSchema.Get() });

        /// <summary>Get current configuration as JSON (secrets included; LAN-only API).</summary>
        [HttpGet("")]
        [Produces("application/json")]
        public IActionResult Get([FromQuery] string format = null)
        {
            var info = AppInit.GetConfigSourceInfo();
            var fmt = format ?? info.format ?? "yaml";
            var data = AppInit.GetConfigData();
            var content = AppInit.GetConfigContent(format: fmt);

            return ConfigJson(new
            {
                ok = true,
                path = info.path,
                format = info.format,
                displayFormat = fmt,
                exists = info.exists,
                lastModifiedUtc = info.lastModifiedUtc,
                data,
                content,
                schema = ConfigSchema.Get(),
                examplePath = System.IO.File.Exists("Data/example.yaml") ? "Data/example.yaml" : "Data/example.conf",
                sensitiveFields = ConfigSchema.SensitiveFieldNames,
                note = "Полный конфиг. API доступен только из локальной сети."
            });
        }

        /// <summary>Validate configuration without writing to disk.</summary>
        [HttpPost("validate")]
        [Produces("application/json")]
        public async Task<IActionResult> Validate()
        {
            var body = await ReadBodyAsync();
            if (body == null)
                return ConfigJson(new { ok = false, error = "Тело запроса пусто" });

            var (jo, parseError) = ResolveConfigPayload(body);
            if (jo == null)
                return ConfigJson(new { ok = false, error = parseError });

            var result = AppInit.ValidateConfigObject(jo);
            return ConfigJson(new
            {
                ok = result.ok,
                error = result.error,
                errors = result.errors,
                warnings = result.warnings
            });
        }

        /// <summary>Compare proposed configuration with current (for save confirmation UI).</summary>
        [HttpPost("diff")]
        [Produces("application/json")]
        public async Task<IActionResult> Diff()
        {
            var body = await ReadBodyAsync();
            if (body == null)
                return ConfigJson(new { ok = false, error = "Тело запроса пусто" });

            var (proposed, parseError) = ResolveConfigPayload(body);
            if (proposed == null)
                return ConfigJson(new { ok = false, error = parseError });

            var validation = AppInit.ValidateConfigObject(proposed);
            var diffs = AppInit.ComputeConfigDiff(proposed);

            return ConfigJson(new
            {
                ok = true,
                diffs,
                changeCount = diffs.Count,
                validation = new
                {
                    ok = validation.ok,
                    error = validation.error,
                    errors = validation.errors,
                    warnings = validation.warnings
                }
            });
        }

        /// <summary>Serialize form data to YAML/JSON text (for form ↔ raw editor sync).</summary>
        [HttpPost("render")]
        [Produces("application/json")]
        public async Task<IActionResult> Render()
        {
            var body = await ReadBodyAsync();
            if (body?.data == null)
                return ConfigJson(new { ok = false, error = "Укажите data" });

            var fmt = body.format ?? "yaml";
            return ConfigJson(new { ok = true, content = AppInit.RenderConfigObject(body.data, fmt), format = fmt });
        }

        /// <summary>Parse YAML/JSON text to structured data (for form ↔ raw editor sync).</summary>
        [HttpPost("parse")]
        [Produces("application/json")]
        public async Task<IActionResult> Parse()
        {
            var body = await ReadBodyAsync();
            if (body == null || string.IsNullOrWhiteSpace(body.content))
                return ConfigJson(new { ok = false, error = "Укажите content" });

            var (jo, parseError) = AppInit.TryParseRequestToJObject(body.content, body.format, null);
            if (jo == null)
                return ConfigJson(new { ok = false, error = parseError ?? "Не удалось разобрать конфигурацию" });

            return ConfigJson(new { ok = true, data = jo });
        }

        /// <summary>Pretty-print config (YAML/JSON) after AppInit normalization.</summary>
        [HttpPost("format")]
        [Produces("application/json")]
        public async Task<IActionResult> Format()
        {
            var body = await ReadBodyAsync();
            if (body == null)
                return ConfigJson(new { ok = false, error = "Тело запроса пусто" });

            var (jo, parseError) = ResolveConfigPayload(body);
            if (jo == null)
                return ConfigJson(new { ok = false, error = parseError ?? "Укажите data или content" });

            var fmt = body.format ?? "yaml";
            var (ok, error, data, content) = AppInit.FormatConfigObject(jo, fmt);
            if (!ok)
                return ConfigJson(new { ok = false, error = error ?? "Ошибка форматирования" });

            return ConfigJson(new { ok = true, data, content, format = fmt });
        }

        /// <summary>Save configuration to init.yaml or init.conf (atomic write, hot-reload ~10s).</summary>
        [HttpPost("")]
        [Produces("application/json")]
        public async Task<IActionResult> Save()
        {
            var body = await ReadBodyAsync();
            if (body == null)
                return ConfigJson(new { ok = false, error = "Тело запроса пусто" });

            var (jo, parseError) = ResolveConfigPayload(body);
            if (jo == null)
                return ConfigJson(new { ok = false, error = parseError ?? "Укажите data или content" });

            var result = AppInit.SaveConfigObject(jo, body.format);
            if (!result.ok)
                return ConfigJson(new { ok = false, error = result.error });

            return ConfigJson(new
            {
                ok = true,
                path = result.info?.path,
                format = result.info?.format,
                lastModifiedUtc = result.info?.lastModifiedUtc,
                message = "Конфигурация сохранена. Изменения применятся автоматически."
            });
        }

        static (JObject jo, string error) ResolveConfigPayload(ConfigSaveRequest body)
        {
            if (body.data != null)
                return (body.data, null);
            if (!string.IsNullOrWhiteSpace(body.content))
                return AppInit.TryParseRequestToJObject(body.content, body.format, null);
            return (null, "Укажите data или content");
        }
    }
}
