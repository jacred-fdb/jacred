using JacRed.Engine.CORE;
using JacRed.Models.Details;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JacRed.Engine
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

        #region Torrents
        async public static Task Torrents()
        {
            await Task.Delay(20_000);

            while (true)
            {
                try
                {
                    if (AppInit.conf == null)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1));
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(AppInit.conf.syncapi))
                    {
                        Console.WriteLine($"\n\nsync: start / {DateTime.Now.ToString(TimeFormat)}");

                        if (lastsync == -1 && File.Exists(LastSyncPath))
                            lastsync = long.Parse(File.ReadAllText(LastSyncPath));

                        var conf = await HttpClient.Get<JObject>($"{AppInit.conf.syncapi}/sync/conf");
                        if (conf != null && conf.ContainsKey("fbd") && conf.Value<bool>("fbd"))
                        {
                            #region Sync.v2
                            if (starsync == -1 && File.Exists(StarSyncPath))
                                starsync = long.Parse(File.ReadAllText(StarSyncPath));

                            bool reset = true;
                            DateTime lastSave = DateTime.Now;

                        next: var root = await HttpClient.Get<Models.Sync.v2.RootObject>($"{AppInit.conf.syncapi}/sync/fdb/torrents?time={lastsync}&start={starsync}", timeoutSeconds: 300, MaxResponseContentBufferSize: 100_000_000);

                            Console.WriteLine($"sync: time={lastsync} ({FormatFileTime(lastsync)}) & start={starsync} ({FormatFileTime(starsync)})");

                            if (root?.collections == null)
                            {
                                if (reset)
                                {
                                    reset = false;
                                    await Task.Delay(TimeSpan.FromMinutes(1));
                                    goto next;
                                }
                            }
                            else if (root.collections.Count > 0)
                            {
                                reset = true;
                                var torrents = new List<TorrentBaseDetails>(root.countread);

                                foreach (var collection in root.collections)
                                {
                                    foreach (var torrent in collection.Value.torrents)
                                    {
                                        if (AppInit.conf.synctrackers != null && torrent.Value.trackerName != null && !AppInit.conf.synctrackers.Contains(torrent.Value.trackerName))
                                            continue;

                                        if (!AppInit.conf.syncsport && torrent.Value.types != null && torrent.Value.types.Contains("sport"))
                                            continue;

                                        torrents.Add(torrent.Value);
                                    }
                                }

                                FileDB.AddOrUpdate(torrents);

                                Console.WriteLine($"sync: processed {torrents.Count} torrents (countread={root.countread}); nextread={root.nextread}");

                                lastsync = root.collections.Last().Value.fileTime;

                                if (root.nextread)
                                {
                                    if (DateTime.Now > lastSave.AddMinutes(5))
                                    {
                                        lastSave = DateTime.Now;
                                        FileDB.SaveChangesToFile();
                                        File.WriteAllText(LastSyncPath, lastsync.ToString());
                                    }

                                    goto next;
                                }

                                starsync = lastsync;
                                File.WriteAllText(StarSyncPath, starsync.ToString());
                            }
                            else if (root.collections.Count == 0)
                            {
                                starsync = lastsync;
                                File.WriteAllText(StarSyncPath, starsync.ToString());
                            }
                            #endregion
                        }
                        else
                        {
                        #region Sync.v1
                        next: var root = await HttpClient.Get<Models.Sync.v1.RootObject>($"{AppInit.conf.syncapi}/sync/torrents?time={lastsync}", timeoutSeconds: 300, MaxResponseContentBufferSize: 100_000_000);
                            if (root?.torrents != null && root.torrents.Count > 0)
                            {
                                FileDB.AddOrUpdate(root.torrents.Select(i => i.value).ToList());

                                lastsync = root.torrents.Last().value.updateTime.ToFileTimeUtc();

                                if (root.take == root.torrents.Count)
                                    goto next;
                            }
                            #endregion
                        }

                        FileDB.SaveChangesToFile();
                        File.WriteAllText(LastSyncPath, lastsync.ToString());

                        Console.WriteLine($"sync: end / {DateTime.Now.ToString(TimeFormat)}");
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1));
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

                    Console.WriteLine($"sync: error / {DateTime.Now.ToString(TimeFormat)} / {ex.Message}");
                }

                await Task.Delay(1000 * Random.Shared.Next(60, 300));
                if (AppInit.conf != null)
                    await Task.Delay(1000 * 60 * (20 > AppInit.conf.timeSync ? 20 : AppInit.conf.timeSync));
            }
        }
        #endregion

        #region Spidr
        async public static Task Spidr()
        {
            while (true)
            {
                try
                {
                    if (AppInit.conf == null)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1));
                        continue;
                    }

                    int spidrMinutes = 20 > AppInit.conf.timeSyncSpidr ? 20 : AppInit.conf.timeSyncSpidr;
                    await Task.Delay(1000 * 60 * spidrMinutes);

                    if (!string.IsNullOrWhiteSpace(AppInit.conf.syncapi) && AppInit.conf.syncspidr)
                    {
                        long lastsync_spidr = -1;

                        var conf = await HttpClient.Get<JObject>($"{AppInit.conf.syncapi}/sync/conf");
                        if (conf != null && conf.ContainsKey("spidr") && conf.Value<bool>("spidr"))
                        {
                            Console.WriteLine($"\n\nsync_spidr: start / {DateTime.Now.ToString(TimeFormat)}");

                        next: var root = await HttpClient.Get<Models.Sync.v2.RootObject>($"{AppInit.conf.syncapi}/sync/fdb/torrents?time={lastsync_spidr}&spidr=true", timeoutSeconds: 300, MaxResponseContentBufferSize: 100_000_000);

                            Console.WriteLine($"sync_spidr: time={lastsync_spidr} ({FormatFileTime(lastsync_spidr)})");

                            if (root?.collections != null && root.collections.Count > 0)
                            {
                                foreach (var collection in root.collections)
                                {
                                    if (collection?.Value?.torrents == null)
                                        continue;
                                    FileDB.AddOrUpdate(collection.Value.torrents.Values);
                                }

                                var lastCollection = root.collections.LastOrDefault();
                                if (lastCollection?.Value != null)
                                    lastsync_spidr = lastCollection.Value.fileTime;

                                if (root.nextread)
                                    goto next;
                            }

                            Console.WriteLine($"sync_spidr: end / {DateTime.Now.ToString(TimeFormat)}");
                        }
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1));
                        continue;
                    }
                }
                catch (Exception ex) { Console.WriteLine($"sync_spidr: error / {DateTime.Now.ToString(TimeFormat)} / {ex.Message}"); }
            }
        }
        #endregion
    }
}
