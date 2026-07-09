using System;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Security
{
    /// <summary>Maps request paths to access policies enforced by JacRedAuthorizationMiddleware.</summary>
    public static partial class JacRedEndpointRegistry
    {
        [GeneratedRegex("^/(api/v1\\.0/conf|sync/)")]
        private static partial Regex PathWhitelistRegex();

        public static JacRedAccessPolicy ResolvePolicy(string path)
        {
            if (IsDevOnlyPath(path)) return JacRedAccessPolicy.DevAdmin;
            if (IsConfigApiPath(path)) return JacRedAccessPolicy.ConfigApi;
            if (IsPathWhitelisted(path)) return JacRedAccessPolicy.Public;
            return JacRedAccessPolicy.ApiKeyWhenConfigured;
        }

        public static bool IsConfigApiPath(string path)
            => !string.IsNullOrEmpty(path)
                && path.StartsWith("/api/v1.0/config", StringComparison.OrdinalIgnoreCase);

        public static bool IsDevOnlyPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("/cron/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/jsondb", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/jsondb/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRestrictedAdminPath(string path)
            => IsDevOnlyPath(path) || IsConfigApiPath(path);

        static bool IsPublicWebPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.Equals("/opensearch.xml", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/manifest.json", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/sw.js", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/img/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/vendor/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/fonts/", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPathWhitelisted(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.Equals("/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/stats", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/stats/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/settings", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/settings/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/health", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/version", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/lastupdatedb", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/openapi.yaml", StringComparison.OrdinalIgnoreCase)
                || IsPublicWebPath(path)
                || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                || PathWhitelistRegex().IsMatch(path);
        }
    }
}
