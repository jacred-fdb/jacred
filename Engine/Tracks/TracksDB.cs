using JacRed.Engine.CORE;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using MonoTorrent;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Engine
{
    public static class TracksDB
    {
        public static void Configuration()
        {
            Console.WriteLine("TracksDB load");

            foreach (var folder1 in Directory.GetDirectories("Data/tracks"))
            {
                foreach (var folder2 in Directory.GetDirectories(folder1))
                {
                    foreach (var file in Directory.GetFiles(folder2))
                    {
                        string infohash = folder1.Substring(12) + folder2.Substring(folder1.Length + 1) + Path.GetFileName(file);

                        try
                        {
                            var res = JsonConvert.DeserializeObject<FfprobeModel>(File.ReadAllText(file));
                            if (res?.streams != null && res.streams.Count > 0)
                                Database.TryAdd(infohash, res);
                        }
                        catch (Exception) { }
                    }
                }
            }
        }

        static readonly object _serverPickLock = new object();
        static Random _random = new Random();

        static ConcurrentDictionary<string, FfprobeModel> Database = new ConcurrentDictionary<string, FfprobeModel>();

        static string pathDb(string infohash, bool createfolder = false)
        {
            string folder = $"Data/tracks/{infohash.Substring(0, 2)}/{infohash[2]}";

            if (createfolder)
                Directory.CreateDirectory(folder);

            return $"{folder}/{infohash.Substring(3)}";
        }

        public static bool theBad(string[] types)
        {
            if (types == null || types.Length == 0)
                return true;

            if (types.Contains("sport") || types.Contains("tvshow") || types.Contains("docuserial"))
                return true;

            return false;
        }

        public static List<ffStream> Get(string magnet, string[] types = null, bool onlydb = false)
        {
            if (types != null && theBad(types))
                return null;

            string infohash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
            if (Database.TryGetValue(infohash, out FfprobeModel res))
                return res.streams;

            string path = pathDb(infohash);
            if (!File.Exists(path))
                return null;

            try
            {
                res = JsonConvert.DeserializeObject<FfprobeModel>(File.ReadAllText(path));
                if (res?.streams == null || res.streams.Count == 0)
                    return null;
            }
            catch (Exception) { return null; }

            Database.AddOrUpdate(infohash, res, (k, v) => res);
            return res.streams;
        }


        async public static Task Add(string magnet, string[] types = null)
        {
            if (types != null && theBad(types))
                return;

            var serverList = AppInit.GetTorrserverList();
            if (serverList == null || serverList.Count == 0)
                return;

            string infohash;
            try
            {
                infohash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
            }
            catch (Exception) { return; }

            if (string.IsNullOrEmpty(infohash) || infohash.Length != 40)
                return;

            int serverIndex;
            lock (_serverPickLock)
            {
                serverIndex = _random.Next(0, serverList.Count);
            }
            var (tsuri, tsuser, tspass) = serverList[serverIndex];

            int timeoutMinutes = Math.Max(1, Math.Min(15, AppInit.conf.tracksFfpTimeoutMinutes));
            int pollMs = Math.Max(500, Math.Min(30_000, AppInit.conf.tracksMetadataPollMs));
            int maxAttempts = Math.Max(5, Math.Min(300, AppInit.conf.tracksMetadataMaxAttempts));
            FfprobeModel res = null;

            try
            {
                var existing = await TorrserverClient.GetTorrent(tsuri, infohash, timeoutSeconds: 20, tsuser, tspass);

                if (TorrserverClient.HasMetadata(existing))
                {
                    res = await TorrserverClient.Ffp(tsuri, infohash, fileIndex: 1, timeoutSeconds: Math.Max(60, (timeoutMinutes - 1) * 60), tsuser, tspass);
                }
                else
                {
                    if (existing == null)
                    {
                        string addResp = await TorrserverClient.AddTorrent(tsuri, magnet, timeoutSeconds: 120, tsuser, tspass);
                        if (string.IsNullOrEmpty(addResp))
                            return;
                    }

                    for (int attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        await Task.Delay(pollMs);
                        var status = await TorrserverClient.GetTorrent(tsuri, infohash, timeoutSeconds: 20, tsuser, tspass);
                        if (TorrserverClient.HasMetadata(status))
                            break;
                        if (attempt == maxAttempts - 1)
                            return;
                    }

                    res = await TorrserverClient.Ffp(tsuri, infohash, fileIndex: 1, timeoutSeconds: Math.Max(60, (timeoutMinutes - 1) * 60), tsuser, tspass);
                }
            }
            catch (Exception) { }
            finally
            {
                await TorrserverClient.RemTorrent(tsuri, infohash, 15, tsuser, tspass);
            }

            if (res?.streams == null || res.streams.Count == 0)
                return;

            Database.AddOrUpdate(infohash, res, (k, v) => res);
            try
            {
                string path = pathDb(infohash, createfolder: true);
                await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(res, Formatting.Indented));
            }
            catch (Exception) { }
        }


        public static HashSet<string> Languages(TorrentDetails t, List<ffStream> streams)
        {
            try
            {
                var languages = new HashSet<string>();

                if (t.languages != null)
                {
                    foreach (var l in t.languages)
                        languages.Add(l);
                }

                if (streams != null)
                {
                    foreach (var item in streams)
                    {
                        if (!string.IsNullOrEmpty(item.tags?.language) && item.codec_type == "audio")
                            languages.Add(item.tags.language);
                    }
                }

                if (languages.Count == 0)
                    return null;

                return languages;
            }
            catch (Exception) { return null; }
        }
    }
}
