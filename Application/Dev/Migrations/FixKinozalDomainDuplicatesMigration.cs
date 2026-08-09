using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;

namespace JacRed.Application.Dev.Migrations
{
    /// <summary>
    /// Схлопывает дубли kinozal после смены домена (.tv → .guru) и
    /// переписывает одиночные URL на канонический хост из конфига.
    /// Группировка по details.php?id= — стабильный ID раздачи.
    /// </summary>
    public sealed class FixKinozalDomainDuplicatesMigration : IDevMigration
    {
        public string Name => "fixKinozalDomainDuplicates";

        static readonly Regex RxId = new Regex(@"details\.php\?id=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public object Run()
        {
            int scanned = 0, rewritten = 0, merged = 0, removed = 0;
            string canonicalHost = HostOf(AppInit.conf.Kinozal.host) ?? "kinozal.guru";
            string canonicalBase = (AppInit.conf.Kinozal.host ?? "https://kinozal.guru").TrimEnd('/');

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var groups = new Dictionary<int, List<KeyValuePair<string, TorrentDetails>>>();

                    foreach (var kv in fdb.Database)
                    {
                        var torrent = kv.Value;
                        if (torrent == null)
                            continue;

                        if (!string.Equals(torrent.trackerName, "kinozal", StringComparison.OrdinalIgnoreCase))
                            continue;

                        scanned++;

                        var m = RxId.Match(kv.Key);
                        if (!m.Success || !int.TryParse(m.Groups[1].Value, out int id) || id <= 0)
                            continue;

                        if (!groups.TryGetValue(id, out var list))
                            groups[id] = list = new List<KeyValuePair<string, TorrentDetails>>();

                        list.Add(kv);
                    }

                    var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var toWrite = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);

                    foreach (var pair in groups)
                    {
                        int id = pair.Key;
                        var entries = pair.Value;
                        string canonicalUrl = $"{canonicalBase}/details.php?id={id}";

                        var keep = entries.FirstOrDefault(kv =>
                            string.Equals(HostOf(kv.Key), canonicalHost, StringComparison.OrdinalIgnoreCase));

                        if (keep.Key == null)
                        {
                            keep = entries
                                .OrderByDescending(kv => !string.IsNullOrWhiteSpace(kv.Value.magnet))
                                .ThenByDescending(kv => kv.Value.sid)
                                .ThenByDescending(kv => kv.Value.updateTime)
                                .First();
                        }

                        var keepTorrent = keep.Value;
                        int losersInGroup = 0;

                        foreach (var kv in entries)
                        {
                            if (ReferenceEquals(kv.Value, keepTorrent))
                                continue;

                            MergeFields(keepTorrent, kv.Value);
                            toRemove.Add(kv.Key);
                            losersInGroup++;
                            merged++;
                            removed++;
                        }

                        bool needsRewrite = !string.Equals(keep.Key, canonicalUrl, StringComparison.OrdinalIgnoreCase);
                        if (needsRewrite)
                        {
                            // Канонический ключ мог принадлежать loser — уже в toRemove.
                            if (fdb.Database.ContainsKey(canonicalUrl) && !toRemove.Contains(canonicalUrl))
                            {
                                // Неожиданный конфликт: оставляем текущий ключ.
                                continue;
                            }

                            toRemove.Add(keep.Key);
                            keepTorrent.url = canonicalUrl;
                            toWrite[canonicalUrl] = keepTorrent;
                            rewritten++;
                        }
                        else if (losersInGroup > 0)
                        {
                            keepTorrent.url = canonicalUrl;
                        }
                    }

                    if (toRemove.Count == 0 && toWrite.Count == 0)
                        continue;

                    foreach (string url in toRemove)
                        fdb.Database.Remove(url);

                    foreach (var kv in toWrite)
                    {
                        kv.Value.url = kv.Key;
                        fdb.Database[kv.Key] = kv.Value;
                    }

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();

            return new
            {
                ok = true,
                scanned,
                rewritten,
                merged,
                removed,
                canonicalHost
            };
        }

        static void MergeFields(TorrentDetails keep, TorrentDetails other)
        {
            if (other == null)
                return;

            if (other.sid > keep.sid)
            {
                keep.sid = other.sid;
                keep.pir = other.pir;
            }
            else if (other.sid == keep.sid && other.pir > keep.pir)
            {
                keep.pir = other.pir;
            }

            if (other.updateTime > keep.updateTime)
                keep.updateTime = other.updateTime;

            if (string.IsNullOrWhiteSpace(keep.magnet) && !string.IsNullOrWhiteSpace(other.magnet))
                keep.magnet = other.magnet;
        }

        static string HostOf(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try { return new Uri(url).Host; }
            catch (UriFormatException) { return null; }
        }
    }
}
