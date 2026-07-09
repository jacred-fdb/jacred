using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;

namespace JacRed.Application.Dev
{
    public class DevDiagnosticsService : IDevDiagnosticsService
    {

        /// <summary>
        /// Scan DB for corrupt entries (null Value, missing name/originalname/trackerName). Read-only, no changes.
        /// </summary>
        public object FindCorrupt(int sampleSize = 20)
        {

            int totalTorrents = 0;
            int nullValueCount = 0;
            int missingNameCount = 0;
            int missingOriginalnameCount = 0;
            int missingTrackerNameCount = 0;
            var nullValueSample = new List<object>();
            var missingNameSample = new List<object>();
            var missingOriginalnameSample = new List<object>();
            var missingTrackerNameSample = new List<object>();

            foreach (var item in FileDB.masterDb.ToArray())
            {
                var db = FileDB.OpenRead(item.Key, cache: false);
                if (db == null)
                    continue;

                foreach (var kv in db)
                {
                    totalTorrents++;
                    string fdbKey = item.Key;
                    string url = kv.Key;
                    var t = kv.Value;

                    if (t == null)
                    {
                        nullValueCount++;
                        if (nullValueSample.Count < sampleSize)
                            nullValueSample.Add(new { fdbKey, url });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(t.trackerName))
                    {
                        missingTrackerNameCount++;
                        if (missingTrackerNameSample.Count < sampleSize)
                            missingTrackerNameSample.Add(new { fdbKey, url, title = t.title });
                    }
                    if (string.IsNullOrWhiteSpace(t.name))
                    {
                        missingNameCount++;
                        if (missingNameSample.Count < sampleSize)
                            missingNameSample.Add(new { fdbKey, url, title = t.title });
                    }
                    if (string.IsNullOrWhiteSpace(t.originalname))
                    {
                        missingOriginalnameCount++;
                        if (missingOriginalnameSample.Count < sampleSize)
                            missingOriginalnameSample.Add(new { fdbKey, url, title = t.title });
                    }
                }
            }

            return new
            {
                ok = true,
                totalFdbKeys = FileDB.masterDb.Count,
                totalTorrents,
                corrupt = new
                {
                    nullValue = new { count = nullValueCount, sample = nullValueSample },
                    missingName = new { count = missingNameCount, sample = missingNameSample },
                    missingOriginalname = new { count = missingOriginalnameCount, sample = missingOriginalnameSample },
                    missingTrackerName = new { count = missingTrackerNameCount, sample = missingTrackerNameSample }
                }
            };
        }
        /// <summary>
        /// Find duplicate keys X:X (name == originalname after normalization), for example ponies:ponies. Only localhost.
        /// ?tracker=lostfilm — only buckets with torrents of this tracker.
        /// ?excludeNumeric=false — include keys that are purely numeric (1899:1899, 911:911); by default they are excluded, as they are usually valid same-name series.
        /// </summary>
        public object FindDuplicateKeys(string tracker = null, bool excludeNumeric = true)
        {

            var duplicateKeys = new List<object>();
            foreach (var item in FileDB.masterDb.ToArray())
            {
                string key = item.Key;
                int colon = key.IndexOf(':');
                if (colon <= 0 || colon >= key.Length - 1)
                    continue;
                string part1 = key.Substring(0, colon);
                string part2 = key.Substring(colon + 1);
                if (!string.Equals(part1, part2, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (excludeNumeric && part1.Length > 0 && part1.All(char.IsDigit))
                    continue;

                int count = 0;
                try
                {
                    var db = FileDB.OpenRead(key, cache: false);
                    count = db.Count;
                    if (!string.IsNullOrWhiteSpace(tracker))
                    {
                        bool hasTracker = db.Values.Any(t => t != null && string.Equals(t.trackerName, tracker.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (!hasTracker)
                            continue;
                    }
                }
                catch
                {
                    continue;
                }

                duplicateKeys.Add(new { key, count });
            }

            return new { ok = true, count = duplicateKeys.Count, keys = duplicateKeys };
        }
        /// <summary>
        /// Находит торренты с пустыми _sn или _so полями. Только localhost, read-only.
        /// ?sampleSize=20 — количество примеров для каждого типа проблемы.
        /// </summary>
        public object FindEmptySearchFields(int sampleSize = 20)
        {

            int totalTorrents = 0;
            int emptySnCount = 0;
            int emptySoCount = 0;
            int emptyBothCount = 0;
            var emptySnSample = new List<object>();
            var emptySoSample = new List<object>();
            var emptyBothSample = new List<object>();

            foreach (var item in FileDB.masterDb.ToArray())
            {
                var db = FileDB.OpenRead(item.Key, cache: false);
                if (db == null)
                    continue;

                foreach (var kv in db)
                {
                    totalTorrents++;
                    string fdbKey = item.Key;
                    string url = kv.Key;
                    var t = kv.Value;

                    if (t == null)
                        continue;

                    bool hasEmptySn = string.IsNullOrWhiteSpace(t._sn);
                    bool hasEmptySo = string.IsNullOrWhiteSpace(t._so);

                    if (hasEmptySn && hasEmptySo)
                    {
                        emptyBothCount++;
                        if (emptyBothSample.Count < sampleSize)
                            emptyBothSample.Add(new { fdbKey, url, title = t.title, name = t.name, originalname = t.originalname });
                    }
                    else if (hasEmptySn)
                    {
                        emptySnCount++;
                        if (emptySnSample.Count < sampleSize)
                            emptySnSample.Add(new { fdbKey, url, title = t.title, name = t.name, originalname = t.originalname });
                    }
                    else if (hasEmptySo)
                    {
                        emptySoCount++;
                        if (emptySoSample.Count < sampleSize)
                            emptySoSample.Add(new { fdbKey, url, title = t.title, name = t.name, originalname = t.originalname });
                    }
                }
            }

            return new
            {
                ok = true,
                totalFdbKeys = FileDB.masterDb.Count,
                totalTorrents,
                emptySearchFields = new
                {
                    emptySn = new { count = emptySnCount, sample = emptySnSample },
                    emptySo = new { count = emptySoCount, sample = emptySoSample },
                    emptyBoth = new { count = emptyBothCount, sample = emptyBothSample },
                    total = emptySnCount + emptySoCount + emptyBothCount
                }
            };
        }

    }
}
