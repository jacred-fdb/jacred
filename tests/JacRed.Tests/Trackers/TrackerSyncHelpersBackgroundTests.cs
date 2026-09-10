using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers;
using Xunit;

namespace JacRed.Tests.Trackers;

public class TrackerSyncHelpersBackgroundTests
{
    [Fact]
    public async Task RunInBackground_ReturnsOkImmediately_AndSecondCallIsWork()
    {
        var flag = new TrackerWorkFlag();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var first = TrackerSyncHelpers.RunInBackground(
            "test-tracker",
            "ParseAllTask",
            flag,
            checkDisabled: false,
            async ct =>
            {
                started.Set();
                while (!release.IsSet)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(20, ct);
                }
            },
            maxDuration: TimeSpan.FromSeconds(30));

        Assert.Equal(TrackerSyncHelpers.OkResult, first);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        var second = TrackerSyncHelpers.RunInBackground(
            "test-tracker",
            "ParseAllTask",
            flag,
            checkDisabled: false,
            _ => Task.CompletedTask,
            maxDuration: TimeSpan.FromSeconds(30));

        Assert.Equal(TrackerSyncHelpers.WorkResult, second);
        Assert.Contains(TrackerSyncHelpers.GetActiveJobs(), j =>
            j.Tracker == "test-tracker" && j.JobLabel == "ParseAllTask");
        Assert.True(flag.IsBusy);

        release.Set();
        Assert.True(await WaitForFlagFreeAsync(flag, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task RunInBackground_WallClockCancel_StopsDelayLoop()
    {
        var flag = new TrackerWorkFlag();
        using var cancelled = new ManualResetEventSlim(false);

        var result = TrackerSyncHelpers.RunInBackground(
            "test-tracker-cancel",
            "ParseAllTask",
            flag,
            checkDisabled: false,
            async ct =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    cancelled.Set();
                    throw;
                }
            },
            maxDuration: TimeSpan.FromMilliseconds(150));

        Assert.Equal(TrackerSyncHelpers.OkResult, result);
        Assert.True(cancelled.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(await WaitForFlagFreeAsync(flag, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task RunParseAllTaskInBackground_UsesOkConstant()
    {
        var flag = new TrackerWorkFlag();
        using var gate = new ManualResetEventSlim(false);

        var result = TrackerSyncHelpers.RunParseAllTaskInBackground(
            "test-ok",
            flag,
            checkDisabled: false,
            async ct =>
            {
                gate.Wait(ct);
                await Task.CompletedTask;
            },
            maxDuration: TimeSpan.FromSeconds(10));

        Assert.Equal("ok", result);
        gate.Set();
        Assert.True(await WaitForFlagFreeAsync(flag, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ReportProgress_UpdatesGetActiveJobs_WithPagesAndCategory()
    {
        var flag = new TrackerWorkFlag();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var result = TrackerSyncHelpers.RunInBackground(
            "test-progress",
            "ParseAllTask",
            flag,
            checkDisabled: false,
            async ct =>
            {
                started.Set();
                while (!release.IsSet)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(20, ct);
                }
            },
            maxDuration: TimeSpan.FromSeconds(30));

        Assert.Equal(TrackerSyncHelpers.OkResult, result);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        TrackerSyncHelpers.ReportProgress("test-progress", "ParseAllTask", 6, 154, "32", 5);

        var job = Assert.Single(TrackerSyncHelpers.GetActiveJobs(),
            j => j.Tracker == "test-progress" && j.JobLabel == "ParseAllTask");
        Assert.Equal("test-progress:ParseAllTask", job.Key);
        Assert.Equal(6, job.PagesCompleted);
        Assert.Equal(154, job.PagesTotal);
        Assert.Equal("32", job.CurrentCategory);
        Assert.Equal(5, job.CurrentPage);
        Assert.Equal(4, TrackerSyncHelpers.Percent(job.PagesCompleted, job.PagesTotal));
        Assert.Equal("6/154 pages · category 32 · page 5", TrackerSyncHelpers.FormatSummary(job));
        Assert.True(TrackerSyncHelpers.HasActiveJob("test-progress"));
        Assert.True(TrackerSyncHelpers.HasActiveJob("test-progress", "UpdateTasksParse"));
        Assert.False(TrackerSyncHelpers.HasActiveJob("test-progress", "ParseAllTask"));
        Assert.False(TrackerSyncHelpers.HasActiveJob("other-tracker"));

        release.Set();
        Assert.True(await WaitForFlagFreeAsync(flag, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SharedClusterFlag_ParseAllBlocksUpdateTasks()
    {
        var cluster = new TrackerWorkFlag();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var parseAll = TrackerSyncHelpers.RunParseAllTaskInBackground(
            "kinozal-cluster",
            cluster,
            checkDisabled: false,
            async ct =>
            {
                started.Set();
                while (!release.IsSet)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(20, ct);
                }
            },
            maxDuration: TimeSpan.FromSeconds(30));

        Assert.Equal(TrackerSyncHelpers.OkResult, parseAll);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        var update = TrackerSyncHelpers.RunUpdateTasksParseInBackground(
            "kinozal-cluster",
            cluster,
            checkDisabled: false,
            _ => Task.CompletedTask,
            maxDuration: TimeSpan.FromHours(2));

        Assert.Equal(TrackerSyncHelpers.WorkResult, update);
        Assert.True(TrackerSyncHelpers.HasActiveJob("kinozal-cluster", "UpdateTasksParse"));
        Assert.True(cluster.IsBusy);

        release.Set();
        Assert.True(await WaitForFlagFreeAsync(cluster, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SeparateWorkFlags_ParseAllBlocksUpdateTasks()
    {
        var parseAllFlag = new TrackerWorkFlag();
        var updateFlag = new TrackerWorkFlag();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var parseAll = TrackerSyncHelpers.RunParseAllTaskInBackground(
            "test-backfill-mutex",
            parseAllFlag,
            checkDisabled: false,
            async ct =>
            {
                started.Set();
                while (!release.IsSet)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(20, ct);
                }
            },
            maxDuration: TimeSpan.FromSeconds(30));

        Assert.Equal(TrackerSyncHelpers.OkResult, parseAll);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        var update = TrackerSyncHelpers.RunUpdateTasksParseInBackground(
            "test-backfill-mutex",
            updateFlag,
            checkDisabled: false,
            _ => Task.CompletedTask,
            maxDuration: TimeSpan.FromSeconds(30));

        Assert.Equal(TrackerSyncHelpers.WorkResult, update);
        Assert.False(updateFlag.IsBusy);
        Assert.True(parseAllFlag.IsBusy);

        release.Set();
        Assert.True(await WaitForFlagFreeAsync(parseAllFlag, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task RunParseLatest_ReturnsWork_WhenParseAllActive()
    {
        var flag = new TrackerWorkFlag();
        var latest = new TrackerLatestParseLock();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var parseAll = TrackerSyncHelpers.RunParseAllTaskInBackground(
            "test-latest-vs-parseall",
            flag,
            checkDisabled: false,
            async ct =>
            {
                started.Set();
                while (!release.IsSet)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(20, ct);
                }
            },
            maxDuration: TimeSpan.FromSeconds(30));

        Assert.Equal(TrackerSyncHelpers.OkResult, parseAll);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        var latestResult = await TrackerSyncHelpers.RunParseLatestAsync(
            "test-latest-vs-parseall",
            latest,
            checkDisabled: false,
            () => Task.FromResult("ok"));

        Assert.Equal(TrackerSyncHelpers.WorkResult, latestResult);

        release.Set();
        Assert.True(await WaitForFlagFreeAsync(flag, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WaitWhileHourlyParseBusy_UnblocksWhenParseEnds()
    {
        var parseLock = new TrackerParseLock();
        Assert.True(parseLock.TryStart());
        Assert.True(parseLock.IsBusy);

        var waiting = TrackerSyncHelpers.WaitWhileHourlyParseBusy(parseLock);
        await Task.Delay(80);
        Assert.False(waiting.IsCompleted);

        parseLock.End();
        Assert.False(parseLock.IsBusy);
        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ThrottleAsync_SleepsOnlyRemainderSinceNoteRequest()
    {
        const string tracker = "test-throttle-remainder";
        var first = Stopwatch.StartNew();
        await TrackerSyncHelpers.ThrottleAsync(tracker, 200);
        first.Stop();
        Assert.True(first.ElapsedMilliseconds < 80, $"first throttle took {first.ElapsedMilliseconds}ms");

        TrackerSyncHelpers.NoteRequest(tracker);

        var second = Stopwatch.StartNew();
        await TrackerSyncHelpers.ThrottleAsync(tracker, 150);
        second.Stop();
        Assert.True(second.ElapsedMilliseconds >= 80, $"remainder throttle took {second.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ShouldPersistCheckpoint_Every25AndLastPage()
    {
        Assert.False(TrackerSyncHelpers.ShouldPersistCheckpoint(0, 100));
        Assert.False(TrackerSyncHelpers.ShouldPersistCheckpoint(24, 100));
        Assert.True(TrackerSyncHelpers.ShouldPersistCheckpoint(25, 100));
        Assert.True(TrackerSyncHelpers.ShouldPersistCheckpoint(50, 100));
        Assert.True(TrackerSyncHelpers.ShouldPersistCheckpoint(100, 100));
        Assert.True(TrackerSyncHelpers.ShouldPersistCheckpoint(7, 7));
    }

    [Fact]
    public void Percent_AndFormatSummary_HandleEmptyProgress()
    {
        Assert.Null(TrackerSyncHelpers.Percent(0, 0));
        Assert.Equal("running", TrackerSyncHelpers.FormatSummary(new TrackerBackgroundJobInfo
        {
            Key = "kinozal:UpdateTasksParse",
            Tracker = "kinozal",
            JobLabel = "UpdateTasksParse",
            StartedAtUtc = DateTime.UtcNow
        }));
    }

    [Fact]
    public void FormatBackgroundCancelMessage_UsesJobProgress()
    {
        var info = new TrackerBackgroundJobInfo
        {
            Key = "solo-cancel:ParseAllTask",
            Tracker = "solo-cancel",
            JobLabel = "ParseAllTask",
            StartedAtUtc = DateTime.UtcNow
        };
        info.PagesCompleted = 40;
        info.PagesTotal = 100;

        var msg = TrackerSyncHelpers.FormatBackgroundCancelMessage("solo-cancel", "ParseAllTask", info, "shutdown");
        Assert.Contains("cancelled (shutdown)", msg);
        Assert.Contains("pending left=60/100", msg);
    }

    [Fact]
    public void IsStalled_WhenLastActivityOlderThanTimeout()
    {
        var info = new TrackerBackgroundJobInfo
        {
            Key = "stall:ParseAllTask",
            Tracker = "stall",
            JobLabel = "ParseAllTask",
            StartedAtUtc = DateTime.UtcNow
        };
        info.LastActivityUtcTicks = DateTime.UtcNow.AddMinutes(-50).Ticks;
        Assert.True(TrackerSyncHelpers.IsStalled(info, DateTime.UtcNow, TimeSpan.FromMinutes(45)));
        info.LastActivityUtcTicks = DateTime.UtcNow.Ticks;
        Assert.False(TrackerSyncHelpers.IsStalled(info, DateTime.UtcNow, TimeSpan.FromMinutes(45)));
        info.LastActivityUtcTicks = 0;
        Assert.False(TrackerSyncHelpers.IsStalled(info, DateTime.UtcNow, TimeSpan.FromMinutes(45)));
    }

    static async Task<bool> WaitForFlagFreeAsync(TrackerWorkFlag flag, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (flag.TryStart())
            {
                flag.End();
                return true;
            }
            await Task.Delay(25);
        }
        return false;
    }
}
