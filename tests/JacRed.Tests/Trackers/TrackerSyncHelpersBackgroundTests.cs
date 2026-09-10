using System;
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
