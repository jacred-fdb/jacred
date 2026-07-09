using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Linq;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JacRed.Infrastructure.OpenApi
{
    public static class SwaggerExtensions
    {
        public static IServiceCollection AddJacRedSwagger(this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "JacRed API",
                    Version = "v1",
                    Description =
                        "Torrent aggregator API: Jackett-compatible search, native torrent search, stats, sync, config. " +
                        "Interactive UI at <code>/swagger</code>. " +
                        "Static OpenAPI YAML at <a href=\"/openapi.yaml\">/openapi.yaml</a>."
                });

                options.DocInclusionPredicate((_, api) =>
                {
                    var path = (api.RelativePath ?? "").TrimStart('/');
                    if (path.StartsWith("cron/", StringComparison.OrdinalIgnoreCase)) return false;
                    if (path.StartsWith("dev/", StringComparison.OrdinalIgnoreCase)) return false;
                    if (path.StartsWith("jsondb/", StringComparison.OrdinalIgnoreCase)) return false;
                    return true;
                });

                options.ResolveConflictingActions(descriptions => descriptions.First());

                options.CustomOperationIds(apiDesc =>
                {
                    apiDesc.ActionDescriptor.RouteValues.TryGetValue("action", out var action);
                    var method = apiDesc.HttpMethod?.ToLowerInvariant() ?? "get";
                    var route = (apiDesc.RelativePath ?? "root")
                        .Replace("/", "_", StringComparison.Ordinal)
                        .Replace("{", "", StringComparison.Ordinal)
                        .Replace("}", "", StringComparison.Ordinal);
                    return $"{action}_{method}_{route}";
                });

                options.TagActionsBy(api =>
                {
                    var path = (api.RelativePath ?? "").TrimStart('/');
                    if (path.StartsWith("api/v1.0/config", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Config" };
                    if (path.StartsWith("stats", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Stats" };
                    if (path.StartsWith("sync/", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Sync" };
                    if (path.Contains("torznab", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Torznab" };
                    if (path.Contains("indexers", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("torrents", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("qualitys", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Search" };
                    if (path is "health" or "version" or "lastupdatedb" or "openapi.yaml" ||
                        path.StartsWith("api/v1.0/conf", StringComparison.OrdinalIgnoreCase))
                        return new[] { "System" };
                    return new[] { "Web" };
                });

                options.MapType<JObject>(() => new OpenApiSchema
                {
                    Type = "object",
                    AdditionalPropertiesAllowed = true,
                    Description = "JSON object"
                });

                options.MapType<JToken>(() => new OpenApiSchema
                {
                    Type = "object",
                    AdditionalPropertiesAllowed = true
                });

                options.MapType(typeof(System.Collections.Generic.Dictionary<string, string>), () => new OpenApiSchema
                {
                    Type = "object",
                    AdditionalProperties = new OpenApiSchema { Type = "string" }
                });

                options.MapType(typeof(HashSet<int>), () => new OpenApiSchema
                {
                    Type = "array",
                    Items = new OpenApiSchema { Type = "integer" }
                });

                options.MapType(typeof(HashSet<string>), () => new OpenApiSchema
                {
                    Type = "array",
                    Items = new OpenApiSchema { Type = "string" }
                });

                options.SchemaFilter<ReadOnlyPropertySchemaFilter>();

                // JacRed.xml contains malformed XML in cron doc comments (&query etc.) — breaks Swashbuckle at runtime.
                // Static spec: wwwroot/openapi.yaml (also served as /swagger/v1/swagger.json).

                options.AddSecurityDefinition("ApiKeyQuery", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Query,
                    Name = "apikey",
                    Description = "API key in query string"
                });

                options.AddSecurityDefinition("ApiKeyHeader", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                    Description = "API key in X-Api-Key header"
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "API key",
                    Description = "API key as Bearer token"
                });

                options.AddSecurityDefinition("DevKeyHeader", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Dev-Key",
                    Description = "Dev key for /api/v1.0/config, /dev/, /cron/, /jsondb from internet or tunnel"
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKeyQuery" }
                        },
                        Array.Empty<string>()
                    },
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKeyHeader" }
                        },
                        Array.Empty<string>()
                    },
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                        },
                        Array.Empty<string>()
                    }
                });

                // XML comments disabled — see note above.
            });

            return services;
        }

        /// <summary>Skip get-only properties (e.g. RootObject.jacred) that break schema generation.</summary>
        sealed class ReadOnlyPropertySchemaFilter : ISchemaFilter
        {
            public void Apply(OpenApiSchema schema, SchemaFilterContext context)
            {
                if (schema?.Properties == null || context?.Type == null) return;

                foreach (var prop in context.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.CanRead && !prop.CanWrite && schema.Properties.ContainsKey(prop.Name))
                        schema.Properties.Remove(prop.Name);
                }
            }
        }
    }
}
