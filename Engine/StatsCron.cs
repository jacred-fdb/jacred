using System;
using System.Threading.Tasks;

namespace JacRed.Engine
{
    public static class StatsCron
    {
        async public static Task Run()
        {
            await Task.Delay(20_000);

            try { StatsCollector.CollectAndWrite(); }
            catch (Exception ex) { Console.WriteLine($"stats: startup collect error / {ex.Message}"); }

            while (true)
            {
                if (AppInit.conf?.timeStatsUpdate == -1)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));
                    continue;
                }

                var intervalMinutes = AppInit.conf?.timeStatsUpdate ?? 90;
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes));

                try { StatsCollector.CollectAndWrite(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"stats: error / {ex.Message}");
                    if (ex.StackTrace != null)
                        Console.WriteLine(ex.StackTrace);
                }
            }
        }
    }
}
