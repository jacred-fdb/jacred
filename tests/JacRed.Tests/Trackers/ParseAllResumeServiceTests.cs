using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using JacRed.Application.Maintenance;
using JacRed.Infrastructure.Trackers;
using JacRed.Models.tParse;
using Newtonsoft.Json.Linq;
using Xunit;

namespace JacRed.Tests.Trackers;

public class ParseAllResumeServiceTests : IDisposable
{
    readonly List<string> _paths = new();

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                var tmp = path + ".tmp";
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch { }
        }
    }

    [Fact]
    public async Task ResumeAsync_IdleWhenNoCycleOrPending()
    {
        var slug = "resume-" + Guid.NewGuid().ToString("N")[..8];
        var starter = new FakeStarter(slug);
        var service = new ParseAllResumeService(new[] { starter });

        var json = JObject.FromObject(await service.ResumeAsync());
        Assert.Equal(0, json.Value<int>("started"));
        Assert.Equal(TrackerSyncHelpers.IdleResult, json["jobs"]![0]!.Value<string>("result"));
        Assert.Equal(0, starter.Calls);
    }

    [Fact]
    public async Task ResumeAsync_StartsWhenCycleHasPendingPages()
    {
        var slug = "resume-" + Guid.NewGuid().ToString("N")[..8];
        SeedPendingCycle(slug);
        var starter = new FakeStarter(slug);
        var service = new ParseAllResumeService(new[] { starter });

        var json = JObject.FromObject(await service.ResumeAsync());
        Assert.Equal(1, json.Value<int>("started"));
        Assert.Equal(TrackerSyncHelpers.OkResult, json["jobs"]![0]!.Value<string>("result"));
        Assert.Equal(1, starter.Calls);
    }

    [Fact]
    public async Task ResumeAsync_WorkWhenParseAllAlreadyRunning()
    {
        var slug = "resume-" + Guid.NewGuid().ToString("N")[..8];
        SeedPendingCycle(slug);
        var starter = new FakeStarter(slug);
        var service = new ParseAllResumeService(new[] { starter });

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flag = new TrackerWorkFlag();
        var kick = TrackerSyncHelpers.RunParseAllTaskInBackground(slug, flag, checkDisabled: false, async ct =>
        {
            entered.TrySetResult();
            await hold.Task.WaitAsync(ct);
        });
        Assert.Equal(TrackerSyncHelpers.OkResult, kick);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            var json = JObject.FromObject(await service.ResumeAsync());
            Assert.Equal(0, json.Value<int>("started"));
            Assert.Equal(TrackerSyncHelpers.WorkResult, json["jobs"]![0]!.Value<string>("result"));
            Assert.Equal(0, starter.Calls);
        }
        finally
        {
            hold.TrySetResult();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (flag.IsBusy && DateTime.UtcNow < deadline)
                await Task.Delay(25);
        }
    }

    void SeedPendingCycle(string slug)
    {
        var cyclePath = ParseAllCycleStore.CyclePathForTracker(slug);
        var taskPath = ParseAllCycleStore.TaskParsePathForTracker(slug);
        _paths.Add(cyclePath);
        _paths.Add(taskPath);

        var cycle = ParseAllCycleStore.CreateCycle("fp", 1);
        ParseAllCycleStore.SaveState(cyclePath, cycle);
        ParseAllCycleStore.PersistTaskParse(taskPath, new Dictionary<string, List<TaskParse>>
        {
            ["1"] = [new TaskParse(0)]
        });
    }

    sealed class FakeStarter : IParseAllStarter
    {
        public FakeStarter(string trackerName) => TrackerName = trackerName;

        public string TrackerName { get; }

        public int Calls { get; private set; }

        public Task<string> ParseAllTaskAsync()
        {
            Calls++;
            return Task.FromResult(TrackerSyncHelpers.OkResult);
        }
    }
}
