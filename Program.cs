using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Globalization;
using System.Text;
using System.Threading;
using JacRed.Engine;
using System.Threading.Tasks;
using System;
using JacRed.Controllers;
using System.IO;

namespace JacRed
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Directory.CreateDirectory("Data/fdb");
            Directory.CreateDirectory("Data/temp");
            Directory.CreateDirectory("Data/log");
            Directory.CreateDirectory("Data/tracks");

            TracksDB.Configuration();
            SyncController.Configuration();
            ApiController.getFastdb(update: true);

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10));
                    try { ApiController.getFastdb(update: true); } catch { }
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

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(op => op.Listen((AppInit.conf.listenip == "any" ? IPAddress.Any : IPAddress.Parse(AppInit.conf.listenip)), AppInit.conf.listenport))
                    .UseStartup<Startup>();
                });
    }
}
