using JacRed.Models.Details;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

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

        private static readonly ThreadLocal<Random> _random =
            new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

        public static int GetRandomDelay()
        {
            return _random.Value.Next(2000, 5001);
        }

        async public static Task Run(int typetask)
        {
            await Task.Delay(20_000);

            bool firstRun = (typetask == 1); // Для задачи 1 сразу выполняем первый запуск

            while (true)
            {
                if (!firstRun)
                {
                    await Task.Delay(TimeSpan.FromMinutes(typetask == 1 ? AppInit.conf.TracksInterval.task1 : AppInit.conf.TracksInterval.task0 + typetask));
                }
                firstRun = false;

                if (AppInit.conf.tracks == false)
                    continue;

                if (AppInit.conf.tracksmod == 1 && (typetask == 3 || typetask == 4))
                    continue;

                try
                {
                    TracksDB.Log($"start typetask={typetask}");
                    var starttime = DateTime.Now;
                    var torrents = new List<TorrentDetails>();

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
                                    {
                                        isok = t.createTime >= DateTime.UtcNow.AddDays(-1);
                                        break;
                                    }
                                case 2:
                                    {
                                        if (t.createTime >= DateTime.UtcNow.AddDays(-1))
                                            break;

                                        isok = t.createTime >= DateTime.UtcNow.AddMonths(-1);
                                        break;
                                    }
                                case 3:
                                    {
                                        if (t.createTime >= DateTime.UtcNow.AddMonths(-1))
                                            break;

                                        isok = t.createTime >= DateTime.UtcNow.AddYears(-1);
                                        break;
                                    }
                                case 4:
                                    {
                                        if (t.createTime >= DateTime.UtcNow.AddYears(-1) || t.updateTime >= DateTime.UtcNow.AddMonths(-1))
                                            break;

                                        isok = true;
                                        break;
                                    }
                                case 5:
                                    {
                                        if (t.createTime >= DateTime.UtcNow.AddYears(-1))
                                            break;

                                        isok = t.updateTime >= DateTime.UtcNow.AddMonths(-1);
                                        break;
                                    }
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

                                    if (t.ffprobe_tryingdata >= AppInit.conf.tracksatempt)
                                        continue;

                                    if (typetask == 1 || typetask == 2 || t.sid > 0)
                                        torrents.Add(t);

                                    /*
									if (typetask == 3 && t.sid > 0 && t.updateTime >= DateTime.UtcNow.AddMonths(-1))
										torrents.Add(t);
									
									if (typetask == 4 && t.sid > 0 && t.updateTime >= DateTime.UtcNow.AddMonths(-2))
										torrents.Add(t);
									
									if (typetask == 5 && t.sid > 0)
										torrents.Add(t);
									*/
                                }
                                catch { }
                            }
                        }
                    }

                    TracksDB.Log($"typetask={typetask} collected {torrents.Count} torrents to process");

                    foreach (var t in torrents.OrderByDescending(i => i.updateTime))
                    {
                        try
                        {
                            if (!AppInit.conf.tracks)
                            {
                                TracksDB.Log($"end typetask={typetask} Tracks off in settings");
                                break;
                            }
                            if (typetask == 2 && DateTime.Now > starttime.AddDays(10))
                                break;

                            if ((typetask == 3 || typetask == 4 || typetask == 5) && DateTime.Now > starttime.AddMonths(1))
                                break;

                            //if ((typetask != 1 && t.ffprobe_tryingdata >= AppInit.conf.tracksatempt))
                            //	continue;

                            if (TracksDB.Get(t.magnet) == null)
                            {
                                //if (typetask != 1)
                                //	t.ffprobe_tryingdata++;

                                string torrentKey = FileDB.KeyForTorrent(t.name, t.originalname);

                                int delay = GetRandomDelay();
                                await Task.Delay(delay);

                                await TracksDB.Add(t.magnet, t.ffprobe_tryingdata, t.types, torrentKey, typetask);
                                //await TracksDB.Add(t.magnet, t.ffprobe_tryingdata);
                            }
                        }
                        catch { }
                    }

                    TracksDB.Log($"end typetask={typetask} (elapsed {(DateTime.Now - starttime).TotalMinutes:F1}m)");
                }
                catch (Exception ex) { TracksDB.Log($"tracks: error typetask={typetask} / {ex.Message}"); }
            }
        }
    }
}
