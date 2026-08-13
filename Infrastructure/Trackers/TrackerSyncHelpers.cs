// Tracker sync shared helpers — parse lock and cron guard patterns.
//
// ParseAsync: TrackerParseLock + RunParseAsync
// ParseAllTask / UpdateTasksParse: TrackerWorkFlag + RunInBackground (HTTP returns immediately)
// ParseLatest: TrackerLatestParseLock + RunParseLatestAsync

using JacRed.Infrastructure.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Snapshot of an in-process background cron job.</summary>
    public sealed class TrackerBackgroundJobInfo
    {
        public string Key { get; init; }
        public string Tracker { get; init; }
        public string JobLabel { get; init; }
        public DateTime StartedAtUtc { get; init; }
        public long ProgressCurrent;
        public long ProgressTotal;
        public string ProgressDetail;

        /// <summary>Wall-clock limit used for CancelAfter / zombie sweep.</summary>
        internal TimeSpan MaxDuration { get; init; }

        /// <summary>Flag to release when sweeping a non-cooperative zombie.</summary>
        internal TrackerWorkFlag WorkFlag { get; init; }
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

        /// <summary>Extra grace after MaxDuration before force-clearing ActiveJobs / workFlag.</summary>
        public static readonly TimeSpan ZombieSweepGrace = TimeSpan.FromMinutes(5);

        const int ProgressLogEvery = 25;

        static readonly ConcurrentDictionary<string, TrackerBackgroundJobInfo> ActiveJobs =
            new ConcurrentDictionary<string, TrackerBackgroundJobInfo>(StringComparer.OrdinalIgnoreCase);

        static CancellationToken _applicationStopping = CancellationToken.None;

        /// <summary>Link background wall clocks to host shutdown (call once from Program).</summary>
        public static void ConfigureApplicationStopping(CancellationToken applicationStopping)
            => _applicationStopping = applicationStopping;

        public static IReadOnlyList<TrackerBackgroundJobInfo> GetActiveJobs()
            => ActiveJobs.Values
                .OrderBy(j => j.Tracker, StringComparer.OrdinalIgnoreCase)
                .ThenBy(j => j.JobLabel, StringComparer.OrdinalIgnoreCase)
                .Select(j => new TrackerBackgroundJobInfo
                {
                    Key = j.Key,
                    Tracker = j.Tracker,
                    JobLabel = j.JobLabel,
                    StartedAtUtc = j.StartedAtUtc,
                    ProgressCurrent = Interlocked.Read(ref j.ProgressCurrent),
                    ProgressTotal = Interlocked.Read(ref j.ProgressTotal),
                    ProgressDetail = j.ProgressDetail,
                    MaxDuration = j.MaxDuration
                })
                .ToList();

        /// <summary>
        /// Force-clear jobs that outlived CancelAfter (non-cooperative hang).
        /// Does not abort the wedged thread; unsticks cron so a new run can start.
        /// </summary>
        public static int SweepZombieJobs(TimeSpan? grace = null)
        {
            var extra = grace ?? ZombieSweepGrace;
            int swept = 0;
            var now = DateTime.UtcNow;

            foreach (var job in ActiveJobs.Values.ToArray())
            {
                var limit = job.MaxDuration > TimeSpan.Zero ? job.MaxDuration : DefaultParseAllMaxDuration;
                if (now < job.StartedAtUtc + limit + extra)
                    continue;

                if (!ActiveJobs.TryRemove(job.Key, out var removed))
                    continue;

                try { removed.WorkFlag?.End(); } catch { }

                long ageSec = (long)(now - removed.StartedAtUtc).TotalSeconds;
                JacRedLog.Warning(JacRedLogCategories.Trackers,
                    $"{removed.Tracker}: {removed.JobLabel} zombie swept ageSec={ageSec} limitSec={limit.TotalSeconds:F0} progress={Interlocked.Read(ref removed.ProgressCurrent)}/{Interlocked.Read(ref removed.ProgressTotal)}");
                swept++;
            }

            return swept;
        }

        public static void ReportProgress(string trackerName, string jobLabel, long current, long total, string detail = null)
        {
            var key = JobKey(trackerName, jobLabel);
            if (!ActiveJobs.TryGetValue(key, out var info))
                return;

            Interlocked.Exchange(ref info.ProgressCurrent, current);
            Interlocked.Exchange(ref info.ProgressTotal, total);
            if (detail != null)
                info.ProgressDetail = detail;

            if (current == 0 || current == total || current % ProgressLogEvery == 0)
            {
                JacRedLog.Information(JacRedLogCategories.Trackers,
                    $"{trackerName}: {jobLabel} progress={current}/{total}{(string.IsNullOrEmpty(detail) ? "" : $" ({detail})")}");
            }
        }

        static string JobKey(string trackerName, string jobLabel) => $"{trackerName}:{jobLabel}";

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
        /// Linked to application shutdown and an optional wall-clock limit.
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
            var key = JobKey(trackerName, jobLabel);
            var info = new TrackerBackgroundJobInfo
            {
                Key = key,
                Tracker = trackerName,
                JobLabel = jobLabel,
                StartedAtUtc = DateTime.UtcNow,
                MaxDuration = duration,
                WorkFlag = workFlag,
                ProgressDetail = jobLabel == "UpdateTasksParse" ? "running" : null
            };
            ActiveJobs[key] = info;

            _ = Task.Run(async () =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping);
                cts.CancelAfter(duration);
                var token = cts.Token;

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
                        $"{trackerName}: {jobLabel} cancelled (wall-clock limit or shutdown)");
                }
                catch (Exception ex)
                {
                    JacRedLog.Error(JacRedLogCategories.Trackers,
                        $"{trackerName}: {jobLabel} error: {ex.Message}");
                }
                finally
                {
                    ActiveJobs.TryRemove(key, out _);
                    workFlag.End();
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
            => RunInBackground(trackerName, "UpdateTasksParse", workFlag, checkDisabled, async ct =>
            {
                ReportProgress(trackerName, "UpdateTasksParse", 0, 0, "running");
                await action(ct).ConfigureAwait(false);
                ReportProgress(trackerName, "UpdateTasksParse", 1, 1, "done");
            }, maxDuration ?? DefaultUpdateTasksMaxDuration);

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
