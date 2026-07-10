// Tracker sync shared helpers — parse lock and cron guard patterns.
//
// ParseAsync: TrackerParseLock + RunParseAsync
// ParseAllTask: TrackerWorkFlag + RunParseAllTaskAsync
// ParseLatest: TrackerLatestParseLock + RunParseLatestAsync

using JacRed.Infrastructure.Logging;
using System;
using System.Threading;
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

    /// <summary>Work flag for secondary jobs (ParseAllTask).</summary>
    public sealed class TrackerWorkFlag
    {
        volatile bool _work;

        public bool TryStart() => System.Threading.Interlocked.CompareExchange(ref _work, true, false) == false;

        public void End() => System.Threading.Interlocked.Exchange(ref _work, false);
    }

    /// <summary>Semaphore guard for ParseLatest (one concurrent run per tracker).</summary>
    public sealed class TrackerLatestParseLock
    {
        readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public Task<bool> TryEnterAsync(CancellationToken cancellationToken = default)
            => _semaphore.WaitAsync(0, cancellationToken);

        public void Exit() => _semaphore.Release();
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

        public static async Task<string> RunParseAllTaskAsync(
            string trackerName,
            TrackerWorkFlag workFlag,
            bool checkDisabled,
            Func<Task> action,
            CancellationToken cancellationToken = default)
        {
            if (checkDisabled && IsTrackerDisabled(trackerName))
            {
                LogParseSkipped(trackerName, DisabledResult);
                return DisabledResult;
            }

            if (!workFlag.TryStart())
            {
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await action();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }
            finally
            {
                workFlag.End();
            }

            return "ok";
        }

        public static async Task<string> RunParseLatestAsync(
            string trackerName,
            TrackerLatestParseLock latestLock,
            bool checkDisabled,
            Func<Task<string>> buildLogAsync,
            CancellationToken cancellationToken = default)
        {
            if (checkDisabled && IsTrackerDisabled(trackerName))
            {
                LogParseSkipped(trackerName, DisabledResult);
                return DisabledResult;
            }

            if (!await latestLock.TryEnterAsync(cancellationToken))
            {
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            try
            {
                var logText = await buildLogAsync();
                return string.IsNullOrWhiteSpace(logText) ? "ok" : logText;
            }
            finally
            {
                latestLock.Exit();
            }
        }
    }
}
