using JacRed.Infrastructure.Trackers;
using JacRed.Models.Api;
using MonoTorrent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace JacRed.Infrastructure.Indexers
{
    public static class IndexerResultMerger
    {
        public static List<Result> MergeAndSort(params IEnumerable<Result>[] batches) => MergeAndSort(batches, null);

        /// <summary>
        /// <see cref="MergeAndSort(IEnumerable{Result}[])"/>, then <paramref name="moreCopies"/> merged only into the torrents
        /// of <paramref name="batches"/>: their seeders, voices, ffprobe and languages count, the torrent's info is not theirs.
        /// <paramref name="infoHashKey"/> may be <see cref="InfoHashKeys"/> shared with a filter of the same records.
        /// </summary>
        public static List<Result> MergeAndSort(IEnumerable<Result>[] batches, IEnumerable<Result> moreCopies, Func<Result, string> infoHashKey = null)
        {
            infoHashKey ??= InfoHashKey;
            var map = new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);
            // Whether map[key].Seeders came from a tracker with reliable counts.
            var reliableSeeders = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            for (int b = 0; b <= batches.Length; b++)
            {
                var batch = b < batches.Length ? batches[b] : moreCopies;
                if (batch == null) continue;
                bool copiesOnly = b == batches.Length;
                foreach (var item in batch)
                {
                    if (item == null) continue;
                    string key = infoHashKey(item);
                    if (string.IsNullOrEmpty(key))
                    {
                        key = "md5:" + Md5(item.Title + "|" + item.MagnetUri);
                    }

                    if (!map.TryGetValue(key, out var existing))
                    {
                        if (copiesOnly) continue;
                        // Inputs may be shared with other requests (FileDB cache records,
                        // IMemoryCache result lists): merge into a copy, never into the input.
                        map[key] = item.Clone();
                        reliableSeeders[key] = UnreliableSeeders.AnyReliable(item.Tracker);
                        continue;
                    }

                    bool itemReliable = UnreliableSeeders.AnyReliable(item.Tracker);
                    if (itemReliable && !reliableSeeders[key])
                    {
                        existing.Seeders = item.Seeders;
                        existing.Peers = item.Peers;
                        reliableSeeders[key] = true;
                    }
                    else if (itemReliable == reliableSeeders[key] && item.Seeders > existing.Seeders)
                    {
                        existing.Seeders = item.Seeders;
                        existing.Peers = Math.Max(existing.Peers, item.Peers);
                    }

                    if (item.info?.voices != null)
                    {
                        existing.info ??= new TorrentInfo();
                        existing.info.voices ??= new HashSet<string>();
                        foreach (var v in item.info.voices) existing.info.voices.Add(v);
                    }

                    if (existing.ffprobe == null && item.ffprobe != null)
                        existing.ffprobe = item.ffprobe;

                    existing.subs ??= item.subs;

                    if ((existing.languages == null || existing.languages.Count == 0) && item.languages != null && item.languages.Count > 0)
                        existing.languages = item.languages;

                    if (existing.info == null && item.info != null && !copiesOnly)
                        existing.info = item.info.Clone();
                }
            }

            return map.Values.OrderByDescending(r => r.Seeders).ThenByDescending(r => r.Peers).ToList();
        }

        /// <summary><see cref="InfoHashKey"/> that parses the magnet of each record once, for one request's records.</summary>
        internal static Func<Result, string> InfoHashKeys()
        {
            var keys = new Dictionary<Result, string>(ReferenceEqualityComparer.Instance);
            return item =>
            {
                if (item == null) return null;
                if (!keys.TryGetValue(item, out var key)) keys[item] = key = InfoHashKey(item);
                return key;
            };
        }

        internal static string InfoHashKey(Result item)
        {
            try
            {
                var magnet = item.MagnetUri;
                if (string.IsNullOrWhiteSpace(magnet)) return null;
                return MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
            }
            catch
            {
                return null;
            }
        }

        static string Md5(string input)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
