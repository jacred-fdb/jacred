using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Tracks
{
    public static class TracksCron
    {
        private static readonly ThreadLocal<Random> _random =
            new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

        /// <summary>Inter-item delay from <c>tracksdelay</c> with ±10% jitter (min 0).</summary>
        public static int GetInterItemDelayMs()
        {
            int baseMs = Math.Max(0, AppInit.conf.tracksdelay);
            if (baseMs == 0)
                return 0;

            int jitter = Math.Max(1, baseMs / 10);
            return baseMs + _random.Value.Next(-jitter, jitter + 1);
        }

        /// <summary>
        /// Периодический анализ медиа-треков торрентов
        /// </summary>
        /// <param name="typetask">
        /// 1 - день
        /// 2 - месяц
        /// 3 - год
        /// 4 - остальное
        /// 5 - обновления
        /// </param>
        /// <param name="cancellationToken">Host shutdown token.</param>
        async public static Task Run(int typetask, CancellationToken cancellationToken = default)
        {
            bool firstRun = (typetask == 1); // Для задачи 1 сразу выполняем первый запуск

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!firstRun)
                {
                    await Task.Delay(TimeSpan.FromMinutes(typetask == 1 ? AppInit.conf.TracksInterval.task1 : AppInit.conf.TracksInterval.task0 + typetask), cancellationToken);
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
                                    // Tracks DB (Data/tracks + index) is canonical — not FileDB.ffprobe
                                    if (TracksDB.theBad(t.types) || TracksDB.HasTrackForTorrent(t))
                                        continue;

                                    if (t.ffprobe_tryingdata >= AppInit.conf.tracksatempt)
                                        continue;

                                    if (typetask == 1 || typetask == 2 || t.sid > 0)
                                        torrents.Add(t);
                                }
                                catch (Exception ex)
                                {
                                    TracksDB.Log($"typetask={typetask} skip candidate: {ex.Message}", typetask);
                                }
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

                            if (TracksDB.HasTrackForTorrent(t))
                                continue;

                            string torrentKey = FileDB.KeyForTorrent(t.name, t.originalname);

                            int delay = GetInterItemDelayMs();
                            if (delay > 0)
                                await Task.Delay(delay, cancellationToken);

                            await TracksDB.Add(t.magnet, t.ffprobe_tryingdata, t.types, torrentKey, typetask);
                        }
                        catch (Exception ex)
                        {
                            TracksDB.Log($"typetask={typetask} process error: {ex.Message}", typetask);
                        }
                    }

                    TracksDB.Log($"end typetask={typetask} (elapsed {(DateTime.Now - starttime).TotalMinutes:F1}m)");
                }
                catch (Exception ex) { TracksDB.Log($"tracks: error typetask={typetask} / {ex.Message}"); }
            }
        }
    }
}
