using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Trackers;
using JacRed.Models;

namespace JacRed.Infrastructure.Persistence
{
    public partial class FileDB
    {
        #region Cron
        static bool TryEvictCacheEntry(string key)
        {
            if (!openWriteTask.TryGetValue(key, out WriteTaskModel wtm) || Volatile.Read(ref wtm.openconnection) > 0)
                return false;

            if (!openWriteTask.TryRemove(key, out wtm))
                return false;

            try { wtm.db.SaveChangesIfNeeded(); } catch { }
            return true;
        }

        static void WarnStuckOpenConnections()
        {
            try
            {
                var stuck = openWriteTask.ToArray()
                    .Where(i => Volatile.Read(ref i.Value.openconnection) > 0
                                && DateTime.Now > i.Value.create.AddMinutes(30))
                    .Take(5)
                    .ToArray();
                foreach (var item in stuck)
                {
                    string shardPath;
                    try { shardPath = pathDb(item.Key); }
                    catch { shardPath = "?"; }

                    JacRedLog.Warning(JacRedLogCategories.Fdb,
                        $"stuck openconnection={Volatile.Read(ref item.Value.openconnection)} key={item.Key} path={shardPath} ageMin={(DateTime.Now - item.Value.create).TotalMinutes:F0}");
                }
            }
            catch { }
        }

        /// <summary>Remove stale shard write leftovers (path.tmp) older than 1 hour.</summary>
        static void CleanupOrphanShardTemps()
        {
            try
            {
                if (!Directory.Exists("Data/fdb"))
                    return;

                var cutoff = DateTime.UtcNow.AddHours(-1);
                int removed = 0;
                foreach (var tmp in Directory.EnumerateFiles("Data/fdb", "*.tmp", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(tmp) > cutoff)
                            continue;
                        File.Delete(tmp);
                        removed++;
                    }
                    catch { }
                }

                try
                {
                    const string masterTmp = "Data/masterDb.bz.tmp";
                    if (File.Exists(masterTmp) && File.GetLastWriteTimeUtc(masterTmp) <= cutoff)
                    {
                        File.Delete(masterTmp);
                        removed++;
                    }
                }
                catch { }

                if (removed > 0)
                    JacRedLog.Warning(JacRedLogCategories.Fdb, $"removed {removed} orphan shard .tmp file(s)");
            }
            catch { }
        }

        async public static Task Cron(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);

                try
                {
                    // Always: stuck writes + orphan temps + zombie cron jobs (independent of evercache).
                    WarnStuckOpenConnections();
                    CleanupOrphanShardTemps();
                    TrackerSyncHelpers.SweepZombieJobs();

                    if (!AppInit.conf.evercache.enable || 0 >= AppInit.conf.evercache.validHour)
                        continue;

                    int evicted = openWriteTask.ToArray()
                        .Where(i => DateTime.UtcNow > i.Value.lastread.AddHours(AppInit.conf.evercache.validHour))
                        .Count(i => TryEvictCacheEntry(i.Key));
                    if (evicted > 0)
                        JacRedLog.Warning(JacRedLogCategories.Fdb, $"evicted {evicted} cache entries (validHour) / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                catch { }
            }
        }

        async public static Task CronFast(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);

                try
                {
                    // Faster zombie sweep so cron "work" flags clear sooner than the 10m Cron tick.
                    TrackerSyncHelpers.SweepZombieJobs();
                }
                catch { }

                if (!AppInit.conf.evercache.enable || 0 >= AppInit.conf.evercache.validHour)
                    continue;

                try
                {
                    if (openWriteTask.Count > AppInit.conf.evercache.maxOpenWriteTask)
                    {
                        var query = openWriteTask.Where(i => DateTime.Now > i.Value.create.AddMinutes(10));
                        query = query.OrderBy(i => i.Value.countread).ThenBy(i => i.Value.lastread);

                        int dropped = query.Take(AppInit.conf.evercache.dropCacheTake).Count(i => TryEvictCacheEntry(i.Key));
                        if (dropped > 0)
                            JacRedLog.Warning(JacRedLogCategories.Fdb, $"dropped {dropped} cache entries (maxOpenWriteTask) / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    }
                }
                catch { }
            }
        }
        #endregion
    }
}
