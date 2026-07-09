using System;
using System.Collections.Generic;
using System.Linq;

namespace JacRed.Infrastructure.Security
{
    /// <summary>Known HTTP routes and expected middleware policy (source of truth for traceability).</summary>
    public static class JacRedAccessCatalog
    {
        public sealed record RouteEntry(string Path, JacRedAccessPolicy Policy, string Controller, string Notes = null);

        public static IReadOnlyList<RouteEntry> Routes { get; } = new RouteEntry[]
        {
            // Public — web shell & health
            new("/", JacRedAccessPolicy.Public, "HomeController", "index.html"),
            new("/stats", JacRedAccessPolicy.Public, "HomeController", "stats.html shell only"),
            new("/settings", JacRedAccessPolicy.Public, "HomeController", "settings.html shell"),
            new("/opensearch.xml", JacRedAccessPolicy.Public, "HomeController"),
            new("/health", JacRedAccessPolicy.Public, "HealthController"),
            new("/version", JacRedAccessPolicy.Public, "HealthController"),
            new("/lastupdatedb", JacRedAccessPolicy.Public, "HealthController"),
            new("/api/v1.0/conf", JacRedAccessPolicy.Public, "HealthController", "Jackett apikey probe"),
            new("/openapi.yaml", JacRedAccessPolicy.Public, "Startup"),
            new("/swagger", JacRedAccessPolicy.Public, "Swagger"),
            new("/swagger/index.html", JacRedAccessPolicy.Public, "Swagger"),

            // Public — sync whitelist (opensync checked in SyncController)
            new("/sync/conf", JacRedAccessPolicy.Public, "SyncController"),
            new("/sync/fdb", JacRedAccessPolicy.Public, "SyncController", "+ opensync in controller"),
            new("/sync/fdb/torrents", JacRedAccessPolicy.Public, "SyncController", "+ opensync in controller"),
            new("/sync/torrents", JacRedAccessPolicy.Public, "SyncController", "deprecated v1 — returns error"),
            new("/sync/tracks/stats", JacRedAccessPolicy.Public, "SyncController", "+ opensync in controller"),

            // Config API
            new("/api/v1.0/config", JacRedAccessPolicy.ConfigApi, "ConfigController"),
            new("/api/v1.0/config/schema", JacRedAccessPolicy.ConfigApi, "ConfigController"),

            // Dev admin
            new("/dev/updateSize", JacRedAccessPolicy.DevAdmin, "DevMaintenanceController"),
            new("/dev/FindCorrupt", JacRedAccessPolicy.DevAdmin, "DevDiagnosticsController"),
            new("/dev/TracksStats", JacRedAccessPolicy.DevAdmin, "DevTracksController"),
            new("/dev/FixKnabenNames", JacRedAccessPolicy.DevAdmin, "DevMigrationController"),
            new("/jsondb/save", JacRedAccessPolicy.DevAdmin, "DbController"),
            new("/cron/rutor/sync", JacRedAccessPolicy.DevAdmin, "Cron/RutorController"),

            // Search — apikey when configured
            new("/api/v1.0/torrents", JacRedAccessPolicy.ApiKeyWhenConfigured, "TorrentsController"),
            new("/api/v1.0/qualitys", JacRedAccessPolicy.ApiKeyWhenConfigured, "TorrentsController"),
            new("/api/v2.0/indexers/all/results", JacRedAccessPolicy.ApiKeyWhenConfigured, "JackettController"),
            new("/torznab/api", JacRedAccessPolicy.ApiKeyWhenConfigured, "TorznabController"),
            new("/api/v2.0/indexers", JacRedAccessPolicy.ApiKeyWhenConfigured, "TorznabController"),
            new("/api/v1/indexer", JacRedAccessPolicy.ApiKeyWhenConfigured, "TorznabController"),

            // Stats JSON — apikey + openstats in controller
            new("/stats/trackers", JacRedAccessPolicy.ApiKeyWhenConfigured, "StatsController", "+ openstats"),
            new("/stats/meta", JacRedAccessPolicy.ApiKeyWhenConfigured, "StatsController", "+ openstats"),
            new("/stats/torrents", JacRedAccessPolicy.ApiKeyWhenConfigured, "StatsController", "+ openstats"),
        };

        /// <summary>Returns registry mismatches (empty = OK).</summary>
        public static IReadOnlyList<string> VerifyRegistry()
        {
            return Routes
                .Select(r => (r, actual: JacRedEndpointRegistry.ResolvePolicy(r.Path)))
                .Where(x => x.actual != x.r.Policy)
                .Select(x => $"{x.r.Path}: expected {x.r.Policy}, registry {x.actual} ({x.r.Controller})")
                .ToList();
        }
    }
}
