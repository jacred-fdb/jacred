// Tracker sync shared helpers — parse lock and cron guard patterns.
//
// All *SyncService classes use TrackerParseLock + RunParseAsync for ParseAsync (and shared locks for API variants).
// ParseAllTask / ParseLatest still use TrackerWorkFlag or SemaphoreSlim per tracker where applicable.

using JacRed.Infrastructure.Logging;
using System;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Trackers
{
    /// <summary>Per-tracker exclusive parse lock (thread-safe TryStart / End).</summary>
    public sealed class TrackerParseLock
    {
        bool _workParse;
        readonly object _lock = new object();

        public bool TryStart()
        {
            lock (_lock)
            {
                if (_workParse)
                    return false;

                _workParse = true;
                return true;
            }
        }

        public void End()
        {
            lock (_lock)
            {
                _workParse = false;
            }
        }
    }

    /// <summary>Simple volatile work flag for secondary jobs (e.g. ParseAllTask).</summary>
    public sealed class TrackerWorkFlag
    {
        volatile bool _work;

        public bool TryStart() => System.Threading.Interlocked.CompareExchange(ref _work, true, false) == false;

        public void End() => System.Threading.Interlocked.Exchange(ref _work, false);
    }

    public static class TrackerSyncHelpers
    {
        public const string DisabledResult = "disabled";
        public const string WorkResult = "work";

        public static bool IsTrackerDisabled(string trackerName)
        {
            return AppInit.conf?.disable_trackers != null
                && AppInit.conf.disable_trackers.Contains(trackerName, StringComparer.OrdinalIgnoreCase);
        }

        public static void LogParseSkipped(string trackerName, string reason)
        {
            JacRedLog.Debug(JacRedLogCategories.Trackers, $"{trackerName}: parse skipped ({reason})");
        }

        /// <summary>
        /// Runs parse body when tracker is enabled and parse lock is free.
        /// Returns <see cref="DisabledResult"/>, <see cref="WorkResult"/>, or the action result.
        /// </summary>
        public static async Task<string> RunParseAsync(
            string trackerName,
            TrackerParseLock parseLock,
            bool checkDisabled,
            Func<Task<string>> action)
        {
            if (checkDisabled && IsTrackerDisabled(trackerName))
            {
                LogParseSkipped(trackerName, DisabledResult);
                return DisabledResult;
            }

            if (!parseLock.TryStart())
            {
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            try
            {
                return await action();
            }
            finally
            {
                parseLock.End();
            }
        }
    }
}
