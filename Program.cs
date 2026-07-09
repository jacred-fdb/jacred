using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Globalization;
using System.Text;
using JacRed.Controllers;
using System;
using System.IO;
using System.Threading.Tasks;

namespace JacRed
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Display version information on startup
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
                Console.WriteLine($"[fatal] UnhandledException: {e.ExceptionObject}");
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Console.WriteLine($"[fatal] UnobservedTaskException: {e.Exception}");
                e.SetObserved();
            };

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(op => op.Listen((AppInit.conf.listenip == "any" ? IPAddress.Any : IPAddress.Parse(AppInit.conf.listenip)), AppInit.conf.listenport))
                    .UseStartup<Startup>();
                });
    }
}
