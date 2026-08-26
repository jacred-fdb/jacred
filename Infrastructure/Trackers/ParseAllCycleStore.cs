using JacRed.Models.tParse;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace JacRed.Infrastructure.Trackers
{
    /// <summary>Persistent ParseAllTask cycle checkpoint (survives midnight and 6h cancels).</summary>
    public sealed class ParseAllCycleState
    {
        public string CycleId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public string MapFingerprint { get; set; }
        public int MapCount { get; set; }
    }

    public static class ParseAllCycleStore
    {
        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented
        };

        public static string CyclePathForTracker(string trackerSlug)
            => $"Data/temp/{trackerSlug}_parseAllCycle.json";

        public static IEnumerable<string> FlatMapKeys(IReadOnlyDictionary<string, List<TaskParse>> map)
        {
            foreach (var kv in map.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                foreach (var page in kv.Value.OrderBy(x => x.page))
                    yield return $"{kv.Key}/{page.page}";
            }
        }

        public static IEnumerable<string> NestedMapKeys(
            IReadOnlyDictionary<string, Dictionary<string, List<TaskParse>>> map)
        {
            foreach (var cat in map.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                foreach (var arg in cat.Value.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    foreach (var page in arg.Value.OrderBy(x => x.page))
                        yield return $"{cat.Key}/{arg.Key}/{page.page}";
                }
            }
        }

        public static string ComputeFingerprint(IEnumerable<string> canonicalKeys)
        {
            var joined = string.Join("\n", canonicalKeys);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static ParseAllCycleState LoadState(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                return JsonConvert.DeserializeObject<ParseAllCycleState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        public static void SaveState(string path, ParseAllCycleState state)
            => WriteJsonAtomic(path, state);

        public static void WriteJsonAtomic(string path, object value)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(value, JsonSettings);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        public static bool IsPendingInCycle(TaskParse page, ParseAllCycleState cycle)
        {
            if (cycle == null || string.IsNullOrEmpty(cycle.CycleId))
                return true;

            return !string.Equals(page.parseAllCycleId, cycle.CycleId, StringComparison.Ordinal);
        }

        public static int CountPendingInCycle(IEnumerable<TaskParse> pages, ParseAllCycleState cycle)
            => pages.Count(p => IsPendingInCycle(p, cycle));

        public static void MarkDoneInCycle(TaskParse page, ParseAllCycleState cycle)
        {
            page.parseAllCycleId = cycle.CycleId;
            page.updateTime = DateTime.Today;
        }

        public static ParseAllCycleState CreateCycle(string fingerprint, int mapCount)
            => new ParseAllCycleState
            {
                CycleId = Guid.NewGuid().ToString("N"),
                StartedAtUtc = DateTime.UtcNow,
                MapFingerprint = fingerprint,
                MapCount = mapCount
            };

        static void MigrateTodayStamps(IEnumerable<TaskParse> allPages, ParseAllCycleState cycle)
        {
            foreach (var page in allPages)
            {
                if (page.updateTime.Date == DateTime.Today)
                    page.parseAllCycleId = cycle.CycleId;
            }
        }

        static void RefreshMetadata(ParseAllCycleState cycle, string fingerprint, int mapCount)
        {
            cycle.MapFingerprint = fingerprint;
            cycle.MapCount = mapCount;
        }

        /// <summary>Load or rotate cycle for a full ParseAllTask run.</summary>
        public static ParseAllCycleState BeginFullCycle(
            string cyclePath,
            string fingerprint,
            int mapCount,
            IEnumerable<TaskParse> allPages,
            bool rotateIfComplete)
        {
            var pages = allPages as IList<TaskParse> ?? allPages.ToList();
            var state = LoadState(cyclePath);

            if (state == null)
            {
                state = CreateCycle(fingerprint, mapCount);
                MigrateTodayStamps(pages, state);
                SaveState(cyclePath, state);
                return state;
            }

            RefreshMetadata(state, fingerprint, mapCount);

            var pending = CountPendingInCycle(pages, state);
            if (rotateIfComplete && pending == 0)
            {
                state = CreateCycle(fingerprint, mapCount);
                SaveState(cyclePath, state);
                return state;
            }

            SaveState(cyclePath, state);
            return state;
        }

        /// <summary>Load active cycle for ParseLatest (no rotation).</summary>
        public static ParseAllCycleState LoadActiveCycle(
            string cyclePath,
            string fingerprint,
            int mapCount,
            IEnumerable<TaskParse> allPages)
        {
            var pages = allPages as IList<TaskParse> ?? allPages.ToList();
            var state = LoadState(cyclePath);

            if (state == null)
            {
                state = CreateCycle(fingerprint, mapCount);
                MigrateTodayStamps(pages, state);
                SaveState(cyclePath, state);
                return state;
            }

            RefreshMetadata(state, fingerprint, mapCount);
            SaveState(cyclePath, state);
            return state;
        }

        public static void PersistAfterPage(
            string cyclePath,
            ParseAllCycleState cycle,
            string taskParsePath,
            object taskParse,
            bool persistCycle)
        {
            WriteJsonAtomic(taskParsePath, taskParse);
            if (persistCycle && cycle != null)
                SaveState(cyclePath, cycle);
        }

        public static void PersistTaskParse(string taskParsePath, object taskParse)
            => WriteJsonAtomic(taskParsePath, taskParse);

        public static string FormatStartLog(ParseAllCycleState cycle, int pending, int total)
            => $"cycle={cycle.CycleId} pending={pending}/{total} started={cycle.StartedAtUtc:yyyy-MM-dd HH:mm:ss}Z fingerprint={cycle.MapFingerprint?[..Math.Min(12, cycle.MapFingerprint?.Length ?? 0)]}";

        public static (ParseAllCycleState cycle, int mapCount, int pendingCount) BeginFlatFullRun(
            string trackerSlug,
            IReadOnlyDictionary<string, List<TaskParse>> taskParse)
        {
            var allPages = taskParse.SelectMany(t => t.Value).ToList();
            var fingerprint = ComputeFingerprint(FlatMapKeys(taskParse));
            var cyclePath = CyclePathForTracker(trackerSlug);
            var cycle = BeginFullCycle(cyclePath, fingerprint, allPages.Count, allPages, rotateIfComplete: true);
            var pendingCount = CountPendingInCycle(allPages, cycle);
            return (cycle, allPages.Count, pendingCount);
        }

        public static ParseAllCycleState LoadFlatActiveCycle(
            string trackerSlug,
            IReadOnlyDictionary<string, List<TaskParse>> taskParse)
        {
            var allPages = taskParse.SelectMany(t => t.Value).ToList();
            var fingerprint = ComputeFingerprint(FlatMapKeys(taskParse));
            return LoadActiveCycle(CyclePathForTracker(trackerSlug), fingerprint, allPages.Count, allPages);
        }

        public static (ParseAllCycleState cycle, int mapCount, int pendingCount) BeginNestedFullRun(
            string trackerSlug,
            IReadOnlyDictionary<string, Dictionary<string, List<TaskParse>>> taskParse)
        {
            var allPages = taskParse.SelectMany(cat => cat.Value.SelectMany(arg => arg.Value)).ToList();
            var fingerprint = ComputeFingerprint(NestedMapKeys(taskParse));
            var cyclePath = CyclePathForTracker(trackerSlug);
            var cycle = BeginFullCycle(cyclePath, fingerprint, allPages.Count, allPages, rotateIfComplete: true);
            var pendingCount = CountPendingInCycle(allPages, cycle);
            return (cycle, allPages.Count, pendingCount);
        }

        public static ParseAllCycleState LoadNestedActiveCycle(
            string trackerSlug,
            IReadOnlyDictionary<string, Dictionary<string, List<TaskParse>>> taskParse)
        {
            var allPages = taskParse.SelectMany(cat => cat.Value.SelectMany(arg => arg.Value)).ToList();
            var fingerprint = ComputeFingerprint(NestedMapKeys(taskParse));
            return LoadActiveCycle(CyclePathForTracker(trackerSlug), fingerprint, allPages.Count, allPages);
        }
    }
}
