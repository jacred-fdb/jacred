// Tracker sync shared helpers — parse lock and cron guard patterns.
//
// ParseAsync: TrackerParseLock + RunParseAsync
// ParseAllTask / UpdateTasksParse: TrackerWorkFlag + RunInBackground (HTTP returns immediately)
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

    /// <summary>Work flag for secondary jobs (ParseAllTask / UpdateTasksParse).</summary>
    public sealed class TrackerWorkFlag
    {
        int _work;

        public bool TryStart() => Interlocked.CompareExchange(ref _work, 1, 0) == 0;

        public void End() => Interlocked.Exchange(ref _work, 0);
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
        public const string OkResult = "ok";

        /// <summary>Default wall-clock limit for background ParseAllTask jobs.</summary>
        public static readonly TimeSpan DefaultParseAllMaxDuration = TimeSpan.FromHours(6);

        /// <summary>Default wall-clock limit for background UpdateTasksParse jobs.</summary>
        public static readonly TimeSpan DefaultUpdateTasksMaxDuration = TimeSpan.FromMinutes(30);

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
            Func<Task<string>> action,
            CancellationToken cancellationToken = default)
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
                cancellationToken.ThrowIfCancellationRequested();
                return await action();
            }
            finally
            {
                parseLock.End();
            }
        }

        /// <summary>
        /// Starts work on a background task and returns immediately with ok/work/disabled.
        /// Releases <paramref name="workFlag"/> when the background work finishes.
        /// </summary>
        public static string RunInBackground(
            string trackerName,
            string jobLabel,
            TrackerWorkFlag workFlag,
            bool checkDisabled,
            Func<CancellationToken, Task> action,
            TimeSpan? maxDuration = null)
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

            var duration = maxDuration ?? DefaultParseAllMaxDuration;
            var cts = new CancellationTokenSource(duration);
            var token = cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    JacRedLog.Information(JacRedLogCategories.Trackers,
                        $"{trackerName}: {jobLabel} started (background, limit={duration.TotalSeconds:F0}s)");
                    await action(token).ConfigureAwait(false);
                    JacRedLog.Information(JacRedLogCategories.Trackers,
                        $"{trackerName}: {jobLabel} finished");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    JacRedLog.Warning(JacRedLogCategories.Trackers,
                        $"{trackerName}: {jobLabel} cancelled (wall-clock limit or abort)");
                }
                catch (Exception ex)
                {
                    JacRedLog.Error(JacRedLogCategories.Trackers,
                        $"{trackerName}: {jobLabel} error: {ex.Message}");
                }
                finally
                {
                    workFlag.End();
                    cts.Dispose();
                }
            });

            return OkResult;
        }

        public static string RunParseAllTaskInBackground(
            string trackerName,
            TrackerWorkFlag workFlag,
            bool checkDisabled,
            Func<CancellationToken, Task> action,
            TimeSpan? maxDuration = null)
            => RunInBackground(trackerName, "ParseAllTask", workFlag, checkDisabled, action,
                maxDuration ?? DefaultParseAllMaxDuration);

        public static string RunUpdateTasksParseInBackground(
            string trackerName,
            TrackerWorkFlag workFlag,
            bool checkDisabled,
            Func<CancellationToken, Task> action,
            TimeSpan? maxDuration = null)
            => RunInBackground(trackerName, "UpdateTasksParse", workFlag, checkDisabled, action,
                maxDuration ?? DefaultUpdateTasksMaxDuration);

        /// <summary>Synchronous wait variant (tests / manual). Prefer background helpers for HTTP cron.</summary>
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

            return OkResult;
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
                return string.IsNullOrWhiteSpace(logText) ? OkResult : logText;
            }
            finally
            {
                latestLock.Exit();
            }
        }
    }
}
