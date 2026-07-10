using JacRed.Application.Dev;
using JacRed.Application.Dev.Migrations;
using JacRed.Application.Index;
using JacRed.Application.Search;
using JacRed.Configuration;
using JacRed.Controllers;
using JacRed.Infrastructure.Background;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.OpenApi;
using JacRed.Infrastructure.Security;
using JacRed.Infrastructure.Trackers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace JacRed
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("  JacRed - Torrent Aggregator & File Database");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine($"  Version:     {VersionInfo.Version}");
            Console.WriteLine($"  Git SHA:     {VersionInfo.GitSha}");
            Console.WriteLine($"  Git Branch:  {VersionInfo.GitBranch}");
            Console.WriteLine($"  Build Date:  {VersionInfo.BuildDate}");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();

            Directory.CreateDirectory("Data/fdb");
            Directory.CreateDirectory("Data/temp");
            Directory.CreateDirectory("Data/log");
            Directory.CreateDirectory("Data/tracks");

            // masterDb (~58MB) must load synchronously before Kestrel accepts requests
            SyncController.Configuration();

            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                JacRedLog.Error(JacRedLogCategories.Host, $"[fatal] UnhandledException: {e.ExceptionObject}");
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                JacRedLog.Error(JacRedLogCategories.Host, $"[fatal] UnobservedTaskException: {e.Exception}");
                e.SetObserved();
            };

            var builder = WebApplication.CreateBuilder(args);

            builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);

            builder.WebHost.UseKestrel(op =>
                op.Listen(AppInit.conf.listenip == "any" ? IPAddress.Any : IPAddress.Parse(AppInit.conf.listenip), AppInit.conf.listenport));

            builder.Services.AddJacRedConfiguration();
            builder.Services.AddJacRedSecurity();
            builder.Services.AddJacRedLogging();

            builder.Services.Configure<CookiePolicyOptions>(options =>
            {
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            builder.Services.AddResponseCompression(options =>
            {
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/vnd.apple.mpegurl", "image/svg+xml" });
            });

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.SetIsOriginAllowed(origin => true)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });

            builder.Services.AddControllersWithViews().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            });

            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<IFastDbIndex>(FastDbIndex.Default);

            builder.Services.AddScoped<IJackettSearchService, JackettSearchService>();
            builder.Services.AddScoped<ITorrentQueryService, TorrentQueryService>();
            builder.Services.AddScoped<IDevMaintenanceService, DevMaintenanceService>();
            builder.Services.AddScoped<IDevDiagnosticsService, DevDiagnosticsService>();
            builder.Services.AddScoped<IDevMigrationService, DevMigrationService>();
            builder.Services.AddScoped<FixKnabenNamesMigration>();
            builder.Services.AddScoped<FixBitruNamesMigration>();
            builder.Services.AddScoped<CleanupMigrations>();
            builder.Services.AddScoped<FixAnilibertyUrlsMigration>();
            builder.Services.AddScoped<RemoveDuplicateAnilibertyMigration>();
            builder.Services.AddScoped<FixAnimelayerDuplicatesMigration>();
            builder.Services.AddScoped<ITracksAdminService, TracksAdminService>();

            builder.Services.AddHostedService<FastDbRefreshWorker>();
            builder.Services.AddHostedService<SyncWorker>();
            builder.Services.AddHostedService<TrackersWorker>();
            builder.Services.AddHostedService<StatsWorker>();
            builder.Services.AddHostedService<FileDbWorker>();
            builder.Services.AddHostedService<TracksWorker>();

            builder.Services.AddJacRedTrackers();
            builder.Services.AddJacRedSwagger();

            var registryErrors = JacRedAccessCatalog.VerifyRegistry();
            if (registryErrors.Count > 0)
            {
                foreach (var err in registryErrors)
                    JacRedLog.Warning("security", $"registry mismatch: {err}");
            }

            var app = builder.Build();

            JacRedLog.Configure(app.Services.GetRequiredService<ILoggerFactory>());
            JacRedLogSettings.Apply(AppInit.conf);

            if (app.Environment.IsDevelopment())
                app.UseDeveloperExceptionPage();
            else
            {
                app.UseExceptionHandler(errorApp =>
                {
                    errorApp.Run(async context =>
                    {
                        context.Response.StatusCode = 500;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        await context.Response.WriteAsync("{\"error\":\"internal server error\"}");
                    });
                });
            }

            app.Use(async (context, next) =>
            {
                ClientNetworkContext.CaptureOriginalRemoteIp(context);
                await next();
            });

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                ForwardLimit = 1,
                KnownIPNetworks =
                {
                    new System.Net.IPNetwork(IPAddress.Loopback, 8),
                    new System.Net.IPNetwork(IPAddress.IPv6Loopback, 128),
                    new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8),
                    new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12),
                    new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16)
                }
            });

            app.UseCors();
            app.UseRouting();
            app.UseResponseCompression();

            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? "";
                if (path.Equals("/openapi.yaml", StringComparison.OrdinalIgnoreCase))
                {
                    var yamlPath = OpenApiSpecHelper.GetYamlPath(app.Environment.ContentRootPath);
                    if (File.Exists(yamlPath))
                    {
                        context.Response.ContentType = "application/yaml; charset=utf-8";
                        await context.Response.SendFileAsync(yamlPath);
                        return;
                    }
                }

                if (path.Equals("/swagger/v1/swagger.json", StringComparison.OrdinalIgnoreCase))
                {
                    if (OpenApiSpecHelper.TryGetOpenApiJson(app.Environment.ContentRootPath, out var json, out var error))
                    {
                        context.Response.ContentType = "application/json; charset=utf-8";
                        await context.Response.WriteAsync(json);
                        return;
                    }

                    JacRedLog.Warning("swagger", $"openapi.yaml → json failed ({error})");
                    context.Response.StatusCode = 503;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync($"{{\"error\":\"{error?.Replace("\"", "\\\"")}\"}}");
                    return;
                }

                await next();
            });

            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi.yaml", "JacRed API (YAML)");
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "JacRed API (JSON)");
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "JacRed API";
            });

            if (AppInit.conf.web)
            {
                var contentTypes = new FileExtensionContentTypeProvider();
                contentTypes.Mappings[".yaml"] = "application/yaml";
                contentTypes.Mappings[".yml"] = "application/yaml";

                app.Use(async (context, next) =>
                {
                    SecurityHeaders.Apply(context);
                    await next();
                });

                app.UseStaticFiles(new StaticFileOptions
                {
                    ContentTypeProvider = contentTypes,
                    OnPrepareResponse = ctx =>
                    {
                        var path = ctx.Context.Request.Path.Value ?? "";
                        if (path.Equals("/sw.js", StringComparison.OrdinalIgnoreCase))
                        {
                            var headers = ctx.Context.Response.Headers;
                            headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                            headers["Pragma"] = "no-cache";
                            headers["Expires"] = "0";
                        }
                    }
                });
            }

            app.UseJacRedSecurity();
            app.MapControllers();

            app.Run();
        }
    }
}
