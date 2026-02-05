using JacRed.Models.Details;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JacRed.Engine
{
    public static class TracksCron
    {
        /// <param name="typetask">
        /// 1 - день
        /// 2 - месяц
        /// 3 - год
        /// 4 - остальное
        /// 5 - обновления
        /// </param>
        async public static Task Run(int typetask)
        {
            await Task.Delay(20_000);

            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(typetask == 1 ? 60 : 180));
                if (AppInit.conf.tracks == false)
                    continue;

                var serverList = AppInit.GetTorrserverList();
                if (serverList == null || serverList.Count == 0)
                    continue;

                if (AppInit.conf.tracksmod == 1 && (typetask == 3 || typetask == 4))
                    continue;

                if (AppInit.conf.tracksOnlyNew && typetask != 1)
                    continue;

                int dayDays = Math.Max(1, Math.Min(365, AppInit.conf.tracksDayWindowDays));
                int monthDays = Math.Max(2, Math.Min(365, AppInit.conf.tracksMonthWindowDays));
                int yearMonths = Math.Max(1, Math.Min(120, AppInit.conf.tracksYearWindowMonths));
                int updatesDays = Math.Max(1, Math.Min(365, AppInit.conf.tracksUpdatesWindowDays));

                try
                {
                    Console.WriteLine($"tracks: start typetask={typetask} servers={serverList.Count} / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    var starttime = DateTime.Now;
                    var torrents = new List<TorrentDetails>();
                    var now = DateTime.UtcNow;

                    foreach (var item in FileDB.masterDb.ToArray())
                    {
                        foreach (var t in FileDB.OpenRead(item.Key, cache: false).Values)
                        {
                            if (string.IsNullOrEmpty(t.magnet))
                                continue;

                            bool isok = false;

                            switch (typetask)
                            {
                                case 1:
                                    isok = t.createTime >= now.AddDays(-dayDays);
                                    break;
                                case 2:
                                    if (t.createTime >= now.AddDays(-1))
                                        break;
                                    isok = t.createTime >= now.AddDays(-monthDays);
                                    break;
                                case 3:
                                    if (t.createTime >= now.AddDays(-monthDays))
                                        break;
                                    isok = t.createTime >= now.AddMonths(-yearMonths);
                                    break;
                                case 4:
                                    if (t.createTime >= now.AddMonths(-yearMonths))
                                        break;
                                    isok = true;
                                    break;
                                case 5:
                                    isok = t.updateTime >= now.AddDays(-updatesDays);
                                    break;
                                default:
                                    break;
                            }

                            if (isok)
                            {
                                try
                                {
                                    if (TracksDB.theBad(t.types) || t.ffprobe != null)
                                        continue;

                                    //var magnetLink = MagnetLink.Parse(t.magnet);
                                    //string hex = magnetLink.InfoHash.ToHex();
                                    //if (hex == null)
                                    //    continue;

                                    if (typetask == 1 || (t.sid > 0 && t.updateTime > DateTime.Today.AddDays(-20)))
                                        torrents.Add(t);
                                }
                                catch { }
                            }
                        }
                    }

                    Console.WriteLine($"tracks: typetask={typetask} collected {torrents.Count} torrents to process");

                    foreach (var t in torrents.OrderByDescending(i => i.updateTime))
                    {
                        try
                        {
                            if (typetask == 2 && DateTime.Now > starttime.AddDays(10))
                                break;

                            if ((typetask == 3 || typetask == 4) && DateTime.Now > starttime.AddMonths(2))
                                break;

                            if ((typetask == 3 || typetask == 4 || typetask == 5) && t.ffprobe_tryingdata >= 3)
                                continue;

                            if (TracksDB.Get(t.magnet) == null)
                            {
                                t.ffprobe_tryingdata++;
                                await TracksDB.Add(t.magnet);
                            }
                        }
                        catch { }
                    }

                    Console.WriteLine($"tracks: end typetask={typetask} / {DateTime.Now:yyyy-MM-dd HH:mm:ss} (elapsed {(DateTime.Now - starttime).TotalMinutes:F1}m)");
                }
                catch (Exception ex) { Console.WriteLine($"tracks: error typetask={typetask} / {ex.Message}"); }
            }
        }

        /// <summary>One-time pass: collect torrents without metadata created in last windowDays, process each. Used by /dev/TracksRunOnce.</summary>
        public static async Task RunOnce(int windowDays)
        {
            if (AppInit.conf?.tracks == false) return;
            var serverList = AppInit.GetTorrserverList();
            if (serverList == null || serverList.Count == 0) return;

            var now = DateTime.UtcNow;
            var torrents = new List<TorrentDetails>();
            foreach (var item in FileDB.masterDb.ToArray())
            {
                foreach (var t in FileDB.OpenRead(item.Key, cache: false).Values)
                {
                    if (string.IsNullOrEmpty(t.magnet)) continue;
                    if (t.createTime < now.AddDays(-windowDays)) continue;
                    if (TracksDB.theBad(t.types) || t.ffprobe != null) continue;
                    if (TracksDB.Get(t.magnet) != null) continue;
                    torrents.Add(t);
                }
            }

            Console.WriteLine($"tracks: RunOnce window={windowDays}d collected {torrents.Count} torrents without metadata");
            foreach (var t in torrents.OrderByDescending(i => i.updateTime))
            {
                try
                {
                    await TracksDB.Add(t.magnet);
                }
                catch { }
            }
            Console.WriteLine($"tracks: RunOnce window={windowDays}d finished");
        }
    }
}
