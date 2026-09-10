// Tracker sync shared helpers — parse lock and cron guard patterns.
//
// ParseAsync: TrackerParseLock + RunParseAsync (never blocked by ParseAll/UpdateTasks)
// ParseAllTask / UpdateTasksParse: TrackerWorkFlag + per-tracker backfill mutex + RunInBackground
// ParseLatest: TrackerLatestParseLock + backfill mutex + RunParseLatestAsync
// ParseAll/ParseLatest yield to hourly parse between pages and throttle with remainder delay.

using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Networking;
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

        public bool IsBusy
        {
            get
            {
                lock (_lock)
                    return _workParse;
            }
        }
    }

    /// <summary>Work flag for secondary jobs (ParseAllTask / UpdateTasksParse).</summary>
    public sealed class TrackerWorkFlag
    {
        int _work;

        public bool TryStart() => Interlocked.CompareExchange(ref _work, 1, 0) == 0;

        public void End() => Interlocked.Exchange(ref _work, 0);

        public bool IsBusy => Volatile.Read(ref _work) == 1;
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
        public long PagesCompleted;
        public long PagesTotal;
        public long LastActivityUtcTicks;
        public string CancelReason;
        public string CurrentCategory;
        public int? CurrentPage;
    }

    public static class TrackerSyncHelpers
    {
        public const string DisabledResult = "disabled";
        public const string WorkResult = "work";
        public const string OkResult = "ok";
        public const string IdleResult = "idle";

        /// <summary>Cancel ParseAll if no activity (progress / yield) for this long.</summary>
        public static readonly TimeSpan ParseAllStallTimeout = TimeSpan.FromMinutes(45);

        /// <summary>Default wall-clock limit for background UpdateTasksParse jobs.</summary>
        public static readonly TimeSpan DefaultUpdateTasksMaxDuration = TimeSpan.FromMinutes(30);

        public const int PersistEveryPages = 25;

        const int ProgressLogEvery = PersistEveryPages;
        const int HourlyParsePollMs = 250;

        static readonly ConcurrentDictionary<string, TrackerBackgroundJobInfo> ActiveJobs =
            new ConcurrentDictionary<string, TrackerBackgroundJobInfo>(StringComparer.OrdinalIgnoreCase);

        static readonly ConcurrentDictionary<string, TrackerWorkFlag> BackfillGates =
            new ConcurrentDictionary<string, TrackerWorkFlag>(StringComparer.OrdinalIgnoreCase);

        static readonly ConcurrentDictionary<string, RateStamp> RateStamps =
            new ConcurrentDictionary<string, RateStamp>(StringComparer.OrdinalIgnoreCase);

        sealed class RateStamp
        {
            public long Ticks;
        }

        static CancellationToken _applicationStopping = CancellationToken.None;

        static TrackerWorkFlag BackfillGate(string trackerName)
            => BackfillGates.GetOrAdd(trackerName, _ => new TrackerWorkFlag());

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
                    PagesCompleted = Interlocked.Read(ref j.PagesCompleted),
                    PagesTotal = Interlocked.Read(ref j.PagesTotal),
                    LastActivityUtcTicks = Interlocked.Read(ref j.LastActivityUtcTicks),
                    CurrentCategory = j.CurrentCategory,
                    CurrentPage = j.CurrentPage
                })
                .ToList();

        /// <summary>
        /// True if this tracker has an in-process ParseAll/UpdateTasks job.
        /// Hourly parse is not listed here — it uses <see cref="TrackerParseLock"/>.
        /// </summary>
        public static bool HasActiveJob(string trackerName, string exceptJobLabel = null)
        {
            if (string.IsNullOrWhiteSpace(trackerName))
                return false;

            foreach (var job in GetActiveJobs())
            {
                if (!string.Equals(job.Tracker, trackerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (exceptJobLabel != null
                    && string.Equals(job.JobLabel, exceptJobLabel, StringComparison.OrdinalIgnoreCase))
                    continue;

                return true;
            }

            return false;
        }

        public static void ReportProgress(string trackerName, string jobLabel, long pagesCompleted, long pagesTotal, string category = null, int? page = null)
        {
            var key = JobKey(trackerName, jobLabel);
            if (!ActiveJobs.TryGetValue(key, out var info))
                return;

            Interlocked.Exchange(ref info.PagesCompleted, pagesCompleted);
            Interlocked.Exchange(ref info.PagesTotal, pagesTotal);
            NoteJobActivity(info);
            if (category != null)
                info.CurrentCategory = category;
            if (page != null)
                info.CurrentPage = page;

            if (pagesCompleted == 0 || pagesCompleted == pagesTotal || pagesCompleted % ProgressLogEvery == 0)
            {
                var loc = string.IsNullOrEmpty(info.CurrentCategory) && info.CurrentPage == null
                    ? ""
                    : $" (category={info.CurrentCategory} page={info.CurrentPage})";
                JacRedLog.Information(JacRedLogCategories.Trackers,
                    $"{trackerName}: {jobLabel} progress={pagesCompleted}/{pagesTotal}{loc}");
            }
        }

        public static int? Percent(long pagesCompleted, long pagesTotal)
            => pagesTotal <= 0 ? null : (int?)Math.Min(100, (int)Math.Round(100.0 * pagesCompleted / pagesTotal));

        public static string FormatSummary(TrackerBackgroundJobInfo job)
        {
            if (job.PagesTotal <= 0)
                return "running";

            var summary = $"{job.PagesCompleted}/{job.PagesTotal} pages";
            if (!string.IsNullOrEmpty(job.CurrentCategory))
                summary += $" · category {job.CurrentCategory}";
            if (job.CurrentPage.HasValue)
                summary += $" · page {job.CurrentPage.Value}";
            return summary;
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

        /// <summary>Pause ParseAll/ParseLatest between pages while hourly parse holds <paramref name="parseLock"/>.</summary>
        public static async Task WaitWhileHourlyParseBusy(
            TrackerParseLock parseLock,
            CancellationToken cancellationToken = default,
            string trackerName = null)
        {
            if (parseLock == null)
                return;

            while (parseLock.IsBusy)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NoteJobActivity(trackerName);
                await Task.Delay(HourlyParsePollMs, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Sleep only the remainder of <paramref name="delayMs"/> since <see cref="NoteRequest"/>.</summary>
        public static async Task ThrottleAsync(string trackerName, int delayMs, CancellationToken cancellationToken = default)
        {
            if (delayMs <= 0 || string.IsNullOrWhiteSpace(trackerName))
                return;

            var stamp = RateStamps.GetOrAdd(trackerName, _ => new RateStamp());
            long last;
            lock (stamp)
                last = stamp.Ticks;

            if (last <= 0)
                return;

            long elapsedMs = (DateTime.UtcNow.Ticks - last) / TimeSpan.TicksPerMillisecond;
            long remain = delayMs - elapsedMs;
            if (remain > 0)
                await Task.Delay((int)Math.Min(remain, int.MaxValue), cancellationToken).ConfigureAwait(false);
        }

        public static void NoteRequest(string trackerName)
        {
            if (string.IsNullOrWhiteSpace(trackerName))
                return;

            var stamp = RateStamps.GetOrAdd(trackerName, _ => new RateStamp());
            lock (stamp)
                stamp.Ticks = DateTime.UtcNow.Ticks;
        }

        public static async Task YieldToHourlyParseAndThrottleAsync(
            TrackerParseLock parseLock,
            string trackerName,
            int delayMs,
            CancellationToken cancellationToken = default)
        {
            await WaitWhileHourlyParseBusy(parseLock, cancellationToken, trackerName).ConfigureAwait(false);
            await ThrottleAsync(trackerName, delayMs, cancellationToken).ConfigureAwait(false);
        }

        public static void NoteJobActivity(string trackerName, string jobLabel = "ParseAllTask")
        {
            if (string.IsNullOrWhiteSpace(trackerName))
                return;

            var key = JobKey(trackerName, jobLabel);
            if (ActiveJobs.TryGetValue(key, out var info))
                NoteJobActivity(info);
        }

        static void NoteJobActivity(TrackerBackgroundJobInfo info)
        {
            if (info == null)
                return;

            Interlocked.Exchange(ref info.LastActivityUtcTicks, DateTime.UtcNow.Ticks);
        }

        public static bool IsStalled(TrackerBackgroundJobInfo info, DateTime utcNow, TimeSpan timeout)
        {
            if (info == null || timeout <= TimeSpan.Zero)
                return false;

            long ticks = Interlocked.Read(ref info.LastActivityUtcTicks);
            if (ticks <= 0)
                return false;

            var last = new DateTime(ticks, DateTimeKind.Utc);
            return utcNow - last > timeout;
        }

        public static string FormatBackgroundCancelMessage(
            string trackerName,
            string jobLabel,
            TrackerBackgroundJobInfo info,
            string reason = null)
        {
            reason ??= info?.CancelReason;
            if (string.IsNullOrWhiteSpace(reason))
                reason = _applicationStopping.IsCancellationRequested ? "shutdown" : "cancelled";

            string reasonText = reason switch
            {
                "stall" => "no progress (stall)",
                "shutdown" => "shutdown",
                "wall" => "wall-clock limit",
                _ => reason
            };

            long completed = info == null ? 0 : Interlocked.Read(ref info.PagesCompleted);
            long slotTotal = info == null ? 0 : Interlocked.Read(ref info.PagesTotal);
            int pendingLeft = (int)Math.Max(0, slotTotal - completed);

            ParseAllCycleState cycle = null;
            if (string.Equals(jobLabel, "ParseAllTask", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(trackerName))
            {
                cycle = ParseAllCycleStore.LoadState(ParseAllCycleStore.CyclePathForTracker(trackerName));
            }

            int total = cycle?.MapCount > 0 ? cycle.MapCount : (int)slotTotal;
            var prefix = $"{trackerName}: {jobLabel} cancelled ({reasonText})";
            if (slotTotal <= 0 && (cycle == null || cycle.MapCount <= 0))
                return prefix;

            return $"{prefix}; {ParseAllCycleStore.FormatCancelLog(cycle, pendingLeft, total)}";
        }

        public static bool ShouldPersistCheckpoint(int completed, int total, int every = PersistEveryPages)
        {
            if (completed <= 0)
                return false;
            if (total > 0 && completed >= total)
                return true;
            return every > 0 && completed % every == 0;
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
        /// Linked to application shutdown. Optional <paramref name="maxDuration"/> is a wall clock
        /// (UpdateTasks). ParseAll omits it and uses a stall watchdog instead.
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

            var backfill = BackfillGate(trackerName);
            if (!backfill.TryStart())
            {
                workFlag.End();
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            var key = JobKey(trackerName, jobLabel);
            var info = new TrackerBackgroundJobInfo
            {
                Key = key,
                Tracker = trackerName,
                JobLabel = jobLabel,
                StartedAtUtc = DateTime.UtcNow
            };
            NoteJobActivity(info);
            ActiveJobs[key] = info;

            _ = Task.Run(async () =>
            {
                using (CloudflareClearance.UseCrawlLane())
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping);
                    if (maxDuration is TimeSpan limit && limit > TimeSpan.Zero)
                        cts.CancelAfter(limit);
                    var token = cts.Token;

                    var stallWatch = string.Equals(jobLabel, "ParseAllTask", StringComparison.OrdinalIgnoreCase)
                        && (maxDuration == null || maxDuration <= TimeSpan.Zero)
                        ? WatchParseAllStallAsync(info, cts, token)
                        : Task.CompletedTask;

                    var limitLabel = maxDuration is TimeSpan d && d > TimeSpan.Zero
                        ? $"limit={d.TotalSeconds:F0}s"
                        : "no wall-clock; stall watchdog on";

                    try
                    {
                        JacRedLog.Information(JacRedLogCategories.Trackers,
                            $"{trackerName}: {jobLabel} started (background, {limitLabel})");
                        await action(token).ConfigureAwait(false);
                        JacRedLog.Information(JacRedLogCategories.Trackers,
                            $"{trackerName}: {jobLabel} finished");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        ActiveJobs.TryGetValue(key, out var job);
                        JacRedLog.Warning(JacRedLogCategories.Trackers,
                            FormatBackgroundCancelMessage(trackerName, jobLabel, job));
                    }
                    catch (Exception ex)
                    {
                        JacRedLog.Error(JacRedLogCategories.Trackers,
                            $"{trackerName}: {jobLabel} error: {ex.Message}");
                    }
                    finally
                    {
                        try { cts.Cancel(); } catch { }
                        try { await stallWatch.ConfigureAwait(false); } catch { }
                        ActiveJobs.TryRemove(key, out _);
                        workFlag.End();
                        backfill.End();
                    }
                }
            });

            return OkResult;
        }

        static async Task WatchParseAllStallAsync(
            TrackerBackgroundJobInfo info,
            CancellationTokenSource cts,
            CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                    if (IsStalled(info, DateTime.UtcNow, ParseAllStallTimeout))
                    {
                        info.CancelReason = "stall";
                        JacRedLog.Warning(JacRedLogCategories.Trackers,
                            $"{info.Tracker}: ParseAllTask stall watchdog — no activity for {ParseAllStallTimeout.TotalMinutes:F0}m");
                        try { cts.Cancel(); } catch { }
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public static string RunParseAllTaskInBackground(
            string trackerName,
            TrackerWorkFlag workFlag,
            bool checkDisabled,
            Func<CancellationToken, Task> action,
            TimeSpan? maxDuration = null)
            => RunInBackground(trackerName, "ParseAllTask", workFlag, checkDisabled, action, maxDuration);

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

            var backfill = BackfillGate(trackerName);
            if (!backfill.TryStart())
            {
                workFlag.End();
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (CloudflareClearance.UseCrawlLane())
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
                backfill.End();
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

            var backfill = BackfillGate(trackerName);
            if (!backfill.TryStart())
            {
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            if (!await latestLock.TryEnterAsync(cancellationToken))
            {
                backfill.End();
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            try
            {
                using (CloudflareClearance.UseCrawlLane())
                {
                    var logText = await buildLogAsync();
                    return string.IsNullOrWhiteSpace(logText) ? OkResult : logText;
                }
            }
            finally
            {
                latestLock.Exit();
                backfill.End();
            }
        }
    }
}
