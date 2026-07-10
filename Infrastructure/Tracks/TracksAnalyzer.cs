using JacRed.Infrastructure.Logging;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using MonoTorrent;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Tracks
{
    internal static partial class TracksAnalyzer
    {
        internal static readonly ConcurrentDictionary<string, FfprobeModel> Database =
            new ConcurrentDictionary<string, FfprobeModel>();

        internal static bool theBad(string[] types)
        {
            if (types == null || types.Length == 0)
                return true;

            if (types.Contains("sport") || types.Contains("tvshow") || types.Contains("docuserial"))
                return true;

            return false;
        }

        /// <param name="magnet">Magnet URI of the torrent.</param>
        /// <param name="types">Content types; sport/tvshow/docuserial are skipped.</param>
        /// <param name="memoryOnly">If true, return streams only when already in the in-memory cache; skip disk lookup.</param>
        internal static List<ffStream> Get(string magnet, string[] types = null, bool memoryOnly = false)
        {
            if (types != null && theBad(types))
                return null;

            string infohash = TracksPathResolver.NormalizeInfohash(MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex());
            if (Database.TryGetValue(infohash, out FfprobeModel res))
                return res.streams;

            if (memoryOnly)
                return null;

            string path = TracksPathResolver.ResolveTrackPath(infohash);
            if (path == null)
                return null;

            try
            {
                res = JsonConvert.DeserializeObject<FfprobeModel>(File.ReadAllText(path));
                if (res?.streams == null || res.streams.Count == 0)
                    return null;
            }
            catch { return null; }

            Database.AddOrUpdate(infohash, res, (k, v) => res);
            TracksIndexManager.RegisterTrackHash(infohash);
            return res.streams;
        }

        internal static void LogAnalysisFailure(int typetask, string infohash, int apiStatusCode, int remaining, string errorMessage)
        {
            var detail = $"Анализ треков для {infohash} без результата. Код ответа API: {apiStatusCode}. Осталось {remaining} попыток.";
            if (!JacRedLogSettings.TracksConsoleDetail)
            {
                var body = $"[task:{typetask}] hash={infohash} code={apiStatusCode} remaining={remaining} msg={TracksFailureMsgKey(errorMessage, apiStatusCode)}";
                JacRedLog.Write(JacRedLogCategories.Tracks, LogLevel.Warning, body);
                if (AppInit.conf?.trackslog == true)
                    TracksDB.LogToFile(detail, typetask);
                return;
            }

            TracksDB.Log(detail, typetask);
        }

        static string TracksFailureMsgKey(string errorMessage, int apiStatusCode)
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                if (errorMessage.Contains("Нет данных", StringComparison.Ordinal)) return "no_track_data";
                if (errorMessage.Contains("таймаут", StringComparison.OrdinalIgnoreCase)) return "timeout";
                if (errorMessage.Contains("JSON", StringComparison.Ordinal)) return "json_error";
            }

            return apiStatusCode switch
            {
                400 => "no_track_data",
                408 => "timeout",
                _ => "error"
            };
        }

        internal static HashSet<string> Languages(TorrentDetails t, List<ffStream> streams)
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
                    foreach (var item in streams.Where(s => !string.IsNullOrEmpty(s.tags?.language) && s.codec_type == "audio"))
                        languages.Add(item.tags.language);
                }

                if (languages.Count == 0)
                    return null;

                return languages;
            }
            catch { return null; }
        }
    }
}
