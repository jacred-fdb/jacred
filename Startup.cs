using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
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
            app.UseDeveloperExceptionPage();


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

            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "JacRed API v1");
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "JacRed API";
            });

            if (AppInit.conf.web)
            {
                app.UseStaticFiles(new StaticFileOptions
                {
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
