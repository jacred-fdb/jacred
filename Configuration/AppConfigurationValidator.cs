using JacRed.Configuration.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace JacRed.Configuration
{
    public static class AppConfigurationValidator
    {
        public static ConfigValidationResult ValidateConfigObject(JObject data)
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

        public static ConfigValidationResult ValidateConfigContent(string content, string format)
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

        public static ConfigValidationResult ValidateConfigModel(AppInit parsed)
        {
            var result = new ConfigValidationResult();
            ConfigSchema.ValidateAgainstSchema(parsed, result.errors, result.warnings);
            result.ok = result.errors.Count == 0;
            if (!result.ok)
                result.error = result.errors[0];
            return result;
        }

        public static JObject NormalizeConfigJObject(JObject proposed)
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
    }
}
