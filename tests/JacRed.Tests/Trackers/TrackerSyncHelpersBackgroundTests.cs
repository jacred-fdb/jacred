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
