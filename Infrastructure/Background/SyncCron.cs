using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Utils;
using JacRed.Infrastructure.Logging;
using JacRed.Models.Details;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Background
{
    public static class SyncCron
    {
        const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
        const string SyncTempDir = "Data/temp";
        static readonly string LastSyncPath = Path.Combine(SyncTempDir, "lastsync.txt");
        static readonly string StarSyncPath = Path.Combine(SyncTempDir, "starsync.txt");
        static long lastsync = -1, starsync = -1;

        static string FormatFileTime(long fileTime)
        {
            if (fileTime < 0) return "-";
            try { return DateTime.FromFileTimeUtc(fileTime).ToLocalTime().ToString(TimeFormat); }
            catch { return fileTime.ToString(); }
        }

        static string FormatElapsed(TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100}";
        }

        #region Torrents
        async public static Task Torrents(CancellationToken cancellationToken = default)
        {
            await Task.Delay(20_000, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (AppInit.conf == null)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(AppInit.conf.syncapi))
                    {
                        var cycleStart = DateTime.Now;
                        var cycleTotal = 0;

                        JacRedLog.Information(JacRedLogCategories.Sync, $"start / {DateTime.Now.ToString(TimeFormat)}");

                        if (lastsync == -1 && File.Exists(LastSyncPath))
                            lastsync = long.Parse(File.ReadAllText(LastSyncPath));

                        var conf = await HttpClient.Get<JObject>($"{AppInit.conf.syncapi}/sync/conf");
                        if (conf == null || !conf.ContainsKey("fbd") || !conf.Value<bool>("fbd"))
                        {
                            JacRedLog.Warning(JacRedLogCategories.Sync, "remote /sync/conf missing fbd — sync v1 removed; upgrade syncapi host to v2");
                        }
                        else
                        {
                            #region Sync.v2
                            if (starsync == -1 && File.Exists(StarSyncPath))
                                starsync = long.Parse(File.ReadAllText(StarSyncPath));

                            JacRedLog.Information(JacRedLogCategories.Sync, $"loaded state lastsync={lastsync} ({FormatFileTime(lastsync)}) starsync={starsync} ({FormatFileTime(starsync)})");

                            bool reset = true;
                            DateTime lastSave = DateTime.Now;
                            int batchIndex = 0;

                        next: batchIndex++;
                            var batchStart = DateTime.Now;
                            var root = await HttpClient.Get<Models.Sync.v2.RootObject>($"{AppInit.conf.syncapi}/sync/fdb/torrents?time={lastsync}&start={starsync}", timeoutSeconds: 300, MaxResponseContentBufferSize: 100_000_000);

                            if (root?.collections == null)
                            {
                                if (reset)
                                {
                                    reset = false;
                                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                                    goto next;
                                }
                            }
                            else if (root.collections.Count > 0)
                            {
                                reset = true;
                                var torrents = new List<TorrentBaseDetails>(root.countread);
                                int filteredByTracker = 0, filteredBySport = 0;

                                foreach (var collection in root.collections)
                                {
                                    foreach (var torrent in collection.Value.torrents)
                                    {
                                        if (AppInit.conf.synctrackers != null && torrent.Value.trackerName != null && !AppInit.conf.synctrackers.Contains(torrent.Value.trackerName))
                                        {
                                            filteredByTracker++;
                                            continue;
                                        }

                                        if (!AppInit.conf.syncsport && torrent.Value.types != null && torrent.Value.types.Contains("sport"))
                                        {
                                            filteredBySport++;
                                            continue;
                                        }

                                        torrents.Add(torrent.Value);
                                    }
                                }

                                if (filteredByTracker > 0 || filteredBySport > 0)
                                    JacRedLog.Information(JacRedLogCategories.Sync, $"  incoming {root.countread}; filtered out {filteredByTracker} by tracker, {filteredBySport} by sport");

                                FileDB.AddOrUpdate(torrents);

                                cycleTotal += torrents.Count;
                                var batchElapsed = DateTime.Now - batchStart;
                                JacRedLog.Information(JacRedLogCategories.Sync, $"[{batchIndex}] time={lastsync} ({FormatFileTime(lastsync)}) | {torrents.Count} torrents, nextread={root.nextread}, {FormatElapsed(batchElapsed)}");

                                lastsync = root.collections.Last().Value.fileTime;

                                if (root.nextread)
                                {
                                    if (DateTime.Now > lastSave.AddMinutes(5))
                                    {
                                        lastSave = DateTime.Now;
                                        FileDB.SaveChangesToFile();
                                        File.WriteAllText(LastSyncPath, lastsync.ToString());
                                        JacRedLog.Information(JacRedLogCategories.Sync, "saved state (lastsync.txt)");
                                    }
                                    goto next;
                                }

                                starsync = lastsync;
                                File.WriteAllText(StarSyncPath, starsync.ToString());
                                JacRedLog.Information(JacRedLogCategories.Sync, "saved state (starsync.txt)");
                            }
                            else if (root.collections.Count == 0)
                            {
                                starsync = lastsync;
                                File.WriteAllText(StarSyncPath, starsync.ToString());
                                JacRedLog.Information(JacRedLogCategories.Sync, "saved state (starsync.txt)");
                            }
                            #endregion
                        }

                        FileDB.SaveChangesToFile();
                        File.WriteAllText(LastSyncPath, lastsync.ToString());

                        var cycleElapsed = DateTime.Now - cycleStart;
                        JacRedLog.Information(JacRedLogCategories.Sync, $"end / {DateTime.Now.ToString(TimeFormat)} (cycle added {cycleTotal} torrents in {FormatElapsed(cycleElapsed)})");
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (lastsync > 0)
                        {
                            FileDB.SaveChangesToFile();
                            File.WriteAllText(LastSyncPath, lastsync.ToString());
                        }
                    }
                    catch { }

                    JacRedLog.Error(JacRedLogCategories.Sync, $"error / {DateTime.Now.ToString(TimeFormat)} / {ex.Message}");
                }

                await Task.Delay(1000 * Random.Shared.Next(60, 300), cancellationToken);
                if (AppInit.conf != null)
                    await Task.Delay(1000 * 60 * (20 > AppInit.conf.timeSync ? 20 : AppInit.conf.timeSync), cancellationToken);
            }
        }
        #endregion

        #region Spidr
        async public static Task Spidr(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (AppInit.conf == null)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                        continue;
                    }

                    int spidrMinutes = 20 > AppInit.conf.timeSyncSpidr ? 20 : AppInit.conf.timeSyncSpidr;
                    await Task.Delay(1000 * 60 * spidrMinutes, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(AppInit.conf.syncapi) && AppInit.conf.syncspidr)
                    {
                        long lastsync_spidr = -1;

                        var conf = await HttpClient.Get<JObject>($"{AppInit.conf.syncapi}/sync/conf");
                        if (conf != null && conf.ContainsKey("spidr") && conf.Value<bool>("spidr"))
                        {
                            var cycleStart = DateTime.Now;
                            var cycleTotal = 0;
                            int batchIndex = 0;

                            JacRedLog.Information(JacRedLogCategories.SyncSpidr, $"start / {DateTime.Now.ToString(TimeFormat)}");

                        next: batchIndex++;
                            var batchStart = DateTime.Now;
                            var root = await HttpClient.Get<Models.Sync.v2.RootObject>($"{AppInit.conf.syncapi}/sync/fdb/torrents?time={lastsync_spidr}&spidr=true", timeoutSeconds: 300, MaxResponseContentBufferSize: 100_000_000);

                            if (root?.collections != null && root.collections.Count > 0)
                            {
                                var batchCount = root.collections.Sum(c => c.Value?.torrents?.Count ?? 0);

                                foreach (var collection in root.collections)
                                {
                                    if (collection?.Value?.torrents == null)
                                        continue;
                                    FileDB.AddOrUpdate(collection.Value.torrents.Values);
                                }

                                cycleTotal += batchCount;
                                var batchElapsed = DateTime.Now - batchStart;
                                JacRedLog.Information(JacRedLogCategories.SyncSpidr, $"[{batchIndex}] time={lastsync_spidr} ({FormatFileTime(lastsync_spidr)}) | {root.collections.Count} collections, {batchCount} torrents, nextread={root.nextread}, {FormatElapsed(batchElapsed)}");

                                var lastCollection = root.collections.LastOrDefault();
                                if (lastCollection?.Value != null)
                                    lastsync_spidr = lastCollection.Value.fileTime;

                                if (root.nextread)
                                {
                                    goto next;
                                }
                            }

                            var cycleElapsed = DateTime.Now - cycleStart;
                            JacRedLog.Information(JacRedLogCategories.SyncSpidr, $"end / {DateTime.Now.ToString(TimeFormat)} (cycle added {cycleTotal} torrents in {FormatElapsed(cycleElapsed)})");
                        }
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                        continue;
                    }
                }
                catch (Exception ex) { JacRedLog.Error(JacRedLogCategories.SyncSpidr, $"error / {DateTime.Now.ToString(TimeFormat)} / {ex.Message}"); }
            }
        }
        #endregion
    }
}
