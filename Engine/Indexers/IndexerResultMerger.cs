using JacRed.Models.Api;
using MonoTorrent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace JacRed.Engine.Indexers
{
    public static class IndexerResultMerger
    {
        public static List<Result> MergeAndSort(params IEnumerable<Result>[] batches)
        {
            var map = new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);

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
                        map[key] = item;
                        continue;
                    }

                    if (item.Seeders > existing.Seeders)
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
                }
            }

            return map.Values.OrderByDescending(r => r.Seeders).ThenByDescending(r => r.Peers).ToList();
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
