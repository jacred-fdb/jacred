using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Globalization;
using System.Text;
using System.Threading;
using JacRed.Engine;
using System.Threading.Tasks;
using System;
using JacRed.Application.Index;
using JacRed.Controllers;
using System.IO;

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

            // masterDb (~58MB) loads here; fast enough to keep before Kestrel
            SyncController.Configuration();

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                try { TracksDB.StartupInit(); }
                catch (IOException ex) { Console.WriteLine($"tracks startup: {ex}"); }
                catch (UnauthorizedAccessException ex) { Console.WriteLine($"tracks startup: {ex}"); }

                // FastDbIndex.Default — same as ApiController.getFastdb shim (Phase 3: IHostedService + DI)
                try { FastDbIndex.Default.Rebuild(); }
                catch (Exception ex) { Console.WriteLine($"fastdb startup: {ex}"); }
            });

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10));
                    try { FastDbIndex.Default.Rebuild(); } catch { }
                }
            });

            ThreadPool.QueueUserWorkItem(async _ => await SyncCron.Torrents());
            ThreadPool.QueueUserWorkItem(async _ => await SyncCron.Spidr());
            ThreadPool.QueueUserWorkItem(async _ => await TrackersCron.Run());
            ThreadPool.QueueUserWorkItem(async _ => await StatsCron.Run());

            ThreadPool.QueueUserWorkItem(async _ => await FileDB.Cron());
            ThreadPool.QueueUserWorkItem(async _ => await FileDB.CronFast());

            ThreadPool.QueueUserWorkItem(async _ => await TracksCron.Run(1));
            ThreadPool.QueueUserWorkItem(async _ => await TracksCron.Run(2));
            ThreadPool.QueueUserWorkItem(async _ => await TracksCron.Run(3));
            ThreadPool.QueueUserWorkItem(async _ => await TracksCron.Run(4));
            ThreadPool.QueueUserWorkItem(async _ => await TracksCron.Run(5));

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
