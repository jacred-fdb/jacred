using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using JacRed.Engine;
using JacRed.Engine.Middlewares;

namespace JacRed
{
    public class Startup
    {
        #region Startup
        public IConfiguration Configuration { get; }

        public static IServiceProvider ApplicationServices { get; private set; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        #endregion

        #region ConfigureServices
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddResponseCompression(options =>
            {
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/vnd.apple.mpegurl", "image/svg+xml" });
            });

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.SetIsOriginAllowed(origin => true)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });

            services.AddControllersWithViews().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
                //options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
                //options.JsonSerializerOptions.WriteIndented = true;
            });

            services.AddJacRedSwagger();
        }
        #endregion


        public void Configure(IApplicationBuilder app)
        {
            ApplicationServices = app.ApplicationServices;

            var env = app.ApplicationServices.GetService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            if (env?.EnvironmentName == Microsoft.Extensions.Hosting.Environments.Development)
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


            // Реальный IP клиента за cloudflared/прокси: доверяем X-Forwarded-For от loopback
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                KnownNetworks =
                {
                    new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Loopback, 8),
                    new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.IPv6Loopback, 128)
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
                    var yamlPath = OpenApiSpecHelper.GetYamlPath(env?.ContentRootPath);
                    if (File.Exists(yamlPath))
                    {
                        context.Response.ContentType = "application/yaml; charset=utf-8";
                        await context.Response.SendFileAsync(yamlPath);
                        return;
                    }
                }

                if (path.Equals("/swagger/v1/swagger.json", StringComparison.OrdinalIgnoreCase))
                {
                    if (OpenApiSpecHelper.TryGetOpenApiJson(env?.ContentRootPath, out var json, out var error))
                    {
                        context.Response.ContentType = "application/json; charset=utf-8";
                        await context.Response.WriteAsync(json);
                        return;
                    }

                    Console.WriteLine($"swagger: openapi.yaml → json failed ({error})");
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
                    ModHeaders.ApplySecurityHeaders(context);
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

            app.UseModHeaders();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
