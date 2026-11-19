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
        public static List<Result> MergeAndSort(params IEnumerable<Result>[] batches)
        {
            var map = new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);
            // Whether map[key].Seeders came from a tracker with reliable counts.
            var reliableSeeders = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var batch in batches)
            {
                if (batch == null) continue;
                foreach (var item in batch)
                {
                    if (item == null) continue;
                    string key = InfoHashKey(item);
                    if (string.IsNullOrEmpty(key))
                    {
                        key = "md5:" + Md5(item.Title + "|" + item.MagnetUri);
                    }

                    if (!map.TryGetValue(key, out var existing))
                    {
                        // Inputs may be shared with other requests (FileDB cache records,
                        // IMemoryCache result lists): merge into a copy, never into the input.
                        map[key] = item.Clone();
                        reliableSeeders[key] = UnreliableSeeders.AnyReliable(item.Tracker);
                        continue;
                    }

                    if (IsLaterCopy(item, existing))
                        TakeRecord(existing, item);

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

                    if (existing.info == null && item.info != null)
                        existing.info = item.info.Clone();
                }
            }

            return map.Values.OrderByDescending(r => r.Seeders).ThenByDescending(r => r.Peers).ToList();
        }

        /// <summary>
        /// A later record of the same torrent on the same tracker: a renamed topic stays in its old bucket
        /// (and under an old domain) with the old title. Copies from other trackers keep the first one's record.
        /// </summary>
        static bool IsLaterCopy(Result item, Result existing) =>
            item.UpdateTime > existing.UpdateTime
            && !string.IsNullOrEmpty(item.Tracker)
            && string.Equals(item.Tracker, existing.Tracker, StringComparison.OrdinalIgnoreCase);

        /// <summary>The record fields of <paramref name="from"/>; seeders, voices and tracks stay merged over all copies.</summary>
        static void TakeRecord(Result into, Result from)
        {
            into.Details = from.Details;
            into.Title = from.Title;
            into.Size = from.Size;
            into.PublishDate = from.PublishDate;
            into.UpdateTime = from.UpdateTime;
            into.Category = from.Category;
            into.CategoryDesc = from.CategoryDesc;
            if (from.info == null) return;

            var voices = into.info?.voices;
            into.info = from.info.Clone();
            if (voices == null) return;
            into.info.voices ??= new HashSet<string>();
            into.info.voices.UnionWith(voices);
        }

        static string InfoHashKey(Result item)
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
