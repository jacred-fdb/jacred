using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Trackers.Ultradox;
using JacRed.Models.Details;

namespace JacRed.Application.Dev.Migrations
{
    /// <summary>
    /// Схлопывает дубли ultradox после смены домена (.onl → .vip, 00N mirrors)
    /// и переписывает одиночные URL на канонический хост из конфига.
    /// Группировка по path+#h= — качества на одной странице остаются разными ключами.
    /// </summary>
    public sealed class FixUltradoxDomainDuplicatesMigration : IDevMigration
    {
        public string Name => "fixUltradoxDomainDuplicates";

        public object Run()
        {
            int scanned = 0, rewritten = 0, merged = 0, removed = 0;
            string canonicalHost = HostOf(AppInit.conf.Ultradox.host) ?? "ultradox.vip";
            string canonicalBase = (AppInit.conf.Ultradox.host ?? "https://ultradox.vip").TrimEnd('/');

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var groups = new Dictionary<string, List<KeyValuePair<string, TorrentDetails>>>(StringComparer.OrdinalIgnoreCase);
                    var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kv in fdb.Database)
                    {
                        var torrent = kv.Value;
                        if (torrent == null)
                            continue;

                        if (!string.Equals(torrent.trackerName, UltradoxParser.TrackerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        scanned++;

                        string pathKey = UltradoxParser.CanonicalPathAndFragment(kv.Key);
                        if (string.IsNullOrEmpty(pathKey) || pathKey == "/")
                            continue;

                        if (!groups.TryGetValue(pathKey, out var list))
                            groups[pathKey] = list = new List<KeyValuePair<string, TorrentDetails>>();

                        list.Add(kv);
                    }

                    var toWrite = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);

                    foreach (var pair in groups)
                    {
                        var entries = pair.Value;
                        string canonicalUrl = UltradoxParser.CanonicalTorrentUrl(canonicalBase, pair.Key);
                        if (string.IsNullOrEmpty(canonicalUrl))
                            continue;

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
                            if (fdb.Database.ContainsKey(canonicalUrl) && !toRemove.Contains(canonicalUrl))
                                continue;

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
