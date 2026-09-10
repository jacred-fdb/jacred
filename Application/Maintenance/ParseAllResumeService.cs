using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Trackers;

namespace JacRed.Application.Maintenance
{
    public sealed class ParseAllResumeSnapshot
    {
        public string tracker { get; set; }
        public bool running { get; set; }
        public string result { get; set; }
        public string cycleId { get; set; }
        public int pending { get; set; }
        public int mapCount { get; set; }
        public DateTime? cycleStartedAtUtc { get; set; }
        public long? pagesCompleted { get; set; }
        public long? pagesTotal { get; set; }
    }

    public sealed class ParseAllResumeService
    {
        readonly IReadOnlyList<IParseAllStarter> _starters;

        public ParseAllResumeService(IEnumerable<IParseAllStarter> starters)
        {
            _starters = starters?.ToArray() ?? Array.Empty<IParseAllStarter>();
        }

        public IReadOnlyList<ParseAllResumeSnapshot> Status()
        {
            return _starters
                .OrderBy(s => s.TrackerName, StringComparer.OrdinalIgnoreCase)
                .Select(Describe)
                .ToList();
        }

        public async Task<object> ResumeAsync()
        {
            var items = new List<ParseAllResumeSnapshot>();
            int started = 0;

            foreach (var starter in _starters.OrderBy(s => s.TrackerName, StringComparer.OrdinalIgnoreCase))
            {
                var snap = Describe(starter);
                if (snap.running)
                {
                    snap.result = TrackerSyncHelpers.WorkResult;
                    items.Add(snap);
                    continue;
                }

                if (string.IsNullOrEmpty(snap.cycleId) || snap.pending <= 0)
                {
                    snap.result = TrackerSyncHelpers.IdleResult;
                    items.Add(snap);
                    continue;
                }

                try
                {
                    snap.result = await starter.ParseAllTaskAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    snap.result = "error";
                    JacRedLog.Error(JacRedLogCategories.Trackers,
                        $"{starter.TrackerName}: ResumeParseAll error: {ex.Message}");
                }

                if (snap.result == TrackerSyncHelpers.OkResult)
                    started++;

                items.Add(snap);
            }

            JacRedLog.Information(JacRedLogCategories.Trackers,
                $"ResumeParseAll started={started} trackers={items.Count}");

            return new { started, jobs = items };
        }

        static ParseAllResumeSnapshot Describe(IParseAllStarter starter)
        {
            var (cycle, pending, mapCount) = ParseAllCycleStore.ReadCycleProgress(starter.TrackerName);
            var job = TrackerSyncHelpers.GetActiveJobs().FirstOrDefault(j =>
                string.Equals(j.Tracker, starter.TrackerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(j.JobLabel, "ParseAllTask", StringComparison.OrdinalIgnoreCase));

            return new ParseAllResumeSnapshot
            {
                tracker = starter.TrackerName,
                running = job != null,
                cycleId = cycle?.CycleId,
                pending = pending,
                mapCount = mapCount,
                cycleStartedAtUtc = cycle?.StartedAtUtc,
                pagesCompleted = job?.PagesCompleted,
                pagesTotal = job?.PagesTotal
            };
        }
    }
}
