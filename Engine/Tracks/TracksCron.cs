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

            bool firstRun = (typetask == 1); // Для задачи 1 сразу выполняем первый запуск

            while (true)
            {
                if (!firstRun)
                {
					await Task.Delay(TimeSpan.FromMinutes(typetask == 1 ? AppInit.conf.TracksInterval.task1 : AppInit.conf.TracksInterval.task0));
                }
                firstRun = false;

                if (AppInit.conf.tracks == false)
                    continue;

                if (AppInit.conf.tracksmod == 1 && (typetask == 3 || typetask == 4))
                    continue;

                try
                {
                    Console.WriteLine($"tracks: start typetask={typetask} / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
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
                                    isok = t.createTime >= DateTime.UtcNow.AddDays(-1);
                                    break;
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
                                        if (t.createTime >= DateTime.UtcNow.AddYears(-1))
                                            break;

                                        isok = true;
                                        break;
                                    }
                                case 5:
                                    {
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
							
							if ((typetask != 1 && t.ffprobe_tryingdata >= AppInit.conf.tracksatempt))
								continue;

                            if (TracksDB.Get(t.magnet) == null)
                            {
                                if (typetask != 1)
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
    }
}
