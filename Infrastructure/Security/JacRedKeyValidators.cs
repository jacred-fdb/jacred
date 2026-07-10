using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Security
{
    public interface IJacRedApiKeyValidator
    {
        bool IsConfigured { get; }
        string GetProvidedKey(HttpContext httpContext);
        bool Validate(HttpContext httpContext);
    }

    public interface IJacRedDevKeyValidator
    {
        bool IsConfigured { get; }
        bool Validate(HttpContext httpContext);
    }

    public static partial class JacRedKeyUtils
    {
        [GeneratedRegex("(\\?|&)apikey=([^&]+)")]
        private static partial Regex ApiKeyQueryRegex();

        [GeneratedRegex("(\\?|&)devkey=([^&]+)")]
        private static partial Regex DevKeyQueryRegex();

        public static bool SecureEquals(string a, string b)
        {
            if (a == null || b == null) return a == b;
            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);
            return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }

        public static string DecodeQueryValue(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            try { return Uri.UnescapeDataString(raw); }
            catch (UriFormatException) { return raw; }
        }

        public static string GetApiKeyFromRequest(HttpContext httpContext)
        {
            var match = ApiKeyQueryRegex().Match(httpContext.Request.QueryString.Value ?? "");
            if (match.Success) return DecodeQueryValue(match.Groups[2].Value);
            if (httpContext.Request.Headers.TryGetValue("X-Api-Key", out var h) && !string.IsNullOrEmpty(h))
                return (h.FirstOrDefault() ?? "").Trim();
            if (httpContext.Request.Headers.TryGetValue("Authorization", out var auth))
            {
                var s = auth.ToString();
                if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return s.Substring(7).Trim();
            }
            return null;
        }

        public static bool DevKeyMatches(HttpContext httpContext, string configuredKey)
        {
            if (string.IsNullOrEmpty(configuredKey)) return true;
            if (httpContext.Request.Headers.TryGetValue("X-Dev-Key", out var h) && !string.IsNullOrEmpty(h))
                return SecureEquals(h.FirstOrDefault() ?? "", configuredKey);
            var match = DevKeyQueryRegex().Match(httpContext.Request.QueryString.Value ?? "");
            if (match.Success) return SecureEquals(DecodeQueryValue(match.Groups[2].Value), configuredKey);
            return false;
        }
    }

    public sealed class JacRedApiKeyValidator : IJacRedApiKeyValidator
    {
        public bool IsConfigured => !string.IsNullOrEmpty(AppInit.conf?.apikey);

        public string GetProvidedKey(HttpContext httpContext)
            => JacRedKeyUtils.GetApiKeyFromRequest(httpContext);

        public bool Validate(HttpContext httpContext)
        {
            if (!IsConfigured) return true;
            var provided = GetProvidedKey(httpContext);
            return !string.IsNullOrEmpty(provided) && JacRedKeyUtils.SecureEquals(provided, AppInit.conf?.apikey);
        }
    }

    public sealed class JacRedDevKeyValidator : IJacRedDevKeyValidator
    {
        public bool IsConfigured => !string.IsNullOrEmpty(AppInit.conf?.devkey);

        public bool Validate(HttpContext httpContext)
            => JacRedKeyUtils.DevKeyMatches(httpContext, AppInit.conf?.devkey);
    }
}
