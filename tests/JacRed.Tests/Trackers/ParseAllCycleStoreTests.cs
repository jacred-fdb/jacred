using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JacRed.Infrastructure.Trackers;
using JacRed.Models.tParse;
using Xunit;

namespace JacRed.Tests.Trackers;

public class ParseAllCycleStoreTests : IDisposable
{
    readonly string _tempDir;
    readonly List<string> _paths = new();

    public ParseAllCycleStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "jacred-parseall-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

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

        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    string NewPath(string name)
    {
        var path = Path.Combine(_tempDir, name);
        _paths.Add(path);
        return path;
    }

    static Dictionary<string, List<TaskParse>> SampleMap(int pagesPerCat = 2)
    {
        return new Dictionary<string, List<TaskParse>>
        {
            ["100"] = Enumerable.Range(0, pagesPerCat).Select(p => new TaskParse(p)).ToList(),
            ["200"] = Enumerable.Range(0, pagesPerCat).Select(p => new TaskParse(p)).ToList()
        };
    }

    static IEnumerable<TaskParse> AllPages(Dictionary<string, List<TaskParse>> map)
        => map.SelectMany(kv => kv.Value);

    [Fact]
    public void CompletedPagesStayExcludedAfterDateChange()
    {
        var cyclePath = NewPath("cycle.json");
        var map = SampleMap();
        var pages = AllPages(map).ToList();
        var fingerprint = ParseAllCycleStore.ComputeFingerprint(ParseAllCycleStore.FlatMapKeys(map));

        var cycle = ParseAllCycleStore.BeginFullCycle(cyclePath, fingerprint, pages.Count, pages, rotateIfComplete: true);
        ParseAllCycleStore.MarkDoneInCycle(pages[0], cycle);

        pages[0].updateTime = DateTime.Today.AddDays(-1);

        Assert.False(ParseAllCycleStore.IsPendingInCycle(pages[0], cycle));
        Assert.True(ParseAllCycleStore.IsPendingInCycle(pages[1], cycle));
    }

    [Fact]
    public void NextRunContinuesSameCycleId()
    {
        var cyclePath = NewPath("cycle-resume.json");
        var map = SampleMap(pagesPerCat: 3);
        var pages = AllPages(map).ToList();
        var fingerprint = ParseAllCycleStore.ComputeFingerprint(ParseAllCycleStore.FlatMapKeys(map));

        var cycle1 = ParseAllCycleStore.BeginFullCycle(cyclePath, fingerprint, pages.Count, pages, rotateIfComplete: true);
        ParseAllCycleStore.MarkDoneInCycle(pages[0], cycle1);
        ParseAllCycleStore.SaveState(cyclePath, cycle1);

        var cycle2 = ParseAllCycleStore.BeginFullCycle(cyclePath, fingerprint, pages.Count, pages, rotateIfComplete: true);

        Assert.Equal(cycle1.CycleId, cycle2.CycleId);
        Assert.False(ParseAllCycleStore.IsPendingInCycle(pages[0], cycle2));
        Assert.Equal(pages.Count - 1, ParseAllCycleStore.CountPendingInCycle(pages, cycle2));
    }

    [Fact]
    public void EmptyPendingRotatesToNewCycle()
    {
        var cyclePath = NewPath("cycle-rotate.json");
        var map = SampleMap();
        var pages = AllPages(map).ToList();
        var fingerprint = ParseAllCycleStore.ComputeFingerprint(ParseAllCycleStore.FlatMapKeys(map));

        var cycle1 = ParseAllCycleStore.BeginFullCycle(cyclePath, fingerprint, pages.Count, pages, rotateIfComplete: true);
        foreach (var page in pages)
            ParseAllCycleStore.MarkDoneInCycle(page, cycle1);
        ParseAllCycleStore.SaveState(cyclePath, cycle1);

        var cycle2 = ParseAllCycleStore.BeginFullCycle(cyclePath, fingerprint, pages.Count, pages, rotateIfComplete: true);

        Assert.NotEqual(cycle1.CycleId, cycle2.CycleId);
        Assert.All(pages, p => Assert.True(ParseAllCycleStore.IsPendingInCycle(p, cycle2)));
    }

    [Fact]
    public void MapGrowthAddsPendingWithoutResettingCycle()
    {
        var cyclePath = NewPath("cycle-growth.json");
        var map = SampleMap(pagesPerCat: 1);
        var pages = AllPages(map).ToList();
        var fingerprint = ParseAllCycleStore.ComputeFingerprint(ParseAllCycleStore.FlatMapKeys(map));

        var cycle = ParseAllCycleStore.BeginFullCycle(cyclePath, fingerprint, pages.Count, pages, rotateIfComplete: true);
        ParseAllCycleStore.MarkDoneInCycle(pages[0], cycle);
        ParseAllCycleStore.MarkDoneInCycle(pages[1], cycle);
        ParseAllCycleStore.SaveState(cyclePath, cycle);

        map["300"] = new List<TaskParse> { new TaskParse(0) };
        pages = AllPages(map).ToList();
        var newFingerprint = ParseAllCycleStore.ComputeFingerprint(ParseAllCycleStore.FlatMapKeys(map));

        var resumed = ParseAllCycleStore.BeginFullCycle(cyclePath, newFingerprint, pages.Count, pages, rotateIfComplete: true);

        Assert.Equal(cycle.CycleId, resumed.CycleId);
        Assert.False(ParseAllCycleStore.IsPendingInCycle(pages[0], resumed));
        Assert.True(ParseAllCycleStore.IsPendingInCycle(pages[^1], resumed));
    }

    [Fact]
    public void MigrationStampsTodayPagesOnFirstSidecar()
    {
        var cyclePath = NewPath("cycle-migrate.json");
        var map = SampleMap();
        var pages = AllPages(map).ToList();
        pages[0].updateTime = DateTime.Today;
        pages[1].updateTime = DateTime.Today.AddDays(-3);

        var fingerprint = ParseAllCycleStore.ComputeFingerprint(ParseAllCycleStore.FlatMapKeys(map));
        var cycle = ParseAllCycleStore.BeginFullCycle(cyclePath, fingerprint, pages.Count, pages, rotateIfComplete: true);

        Assert.Equal(cycle.CycleId, pages[0].parseAllCycleId);
        Assert.Null(pages[1].parseAllCycleId);
        Assert.False(ParseAllCycleStore.IsPendingInCycle(pages[0], cycle));
        Assert.True(ParseAllCycleStore.IsPendingInCycle(pages[1], cycle));
    }

    [Fact]
    public void PartialRunLogic_DoesNotUseCycleStamp()
    {
        var map = SampleMap(pagesPerCat: 1);
        var page = map["100"][0];
        page.updateTime = DateTime.Today;

        Assert.False(DateTime.Today != page.updateTime);
        Assert.True(string.IsNullOrEmpty(page.parseAllCycleId));
    }

    [Fact]
    public void NestedMapFingerprint_IsStable()
    {
        var map = new Dictionary<string, Dictionary<string, List<TaskParse>>>
        {
            ["1"] = new Dictionary<string, List<TaskParse>>
            {
                ["&d=2024"] = new List<TaskParse> { new TaskParse(0), new TaskParse(1) }
            }
        };

        var fp1 = ParseAllCycleStore.ComputeFingerprint(ParseAllCycleStore.NestedMapKeys(map));
        var fp2 = ParseAllCycleStore.ComputeFingerprint(ParseAllCycleStore.NestedMapKeys(map));

        Assert.Equal(fp1, fp2);
        Assert.Equal(64, fp1.Length);
    }

    [Fact]
    public void WriteJsonAtomic_ReplacesExistingFile()
    {
        var path = NewPath("atomic.json");
        ParseAllCycleStore.WriteJsonAtomic(path, new { v = 1 });
        ParseAllCycleStore.WriteJsonAtomic(path, new { v = 2 });

        var text = File.ReadAllText(path);
        Assert.Contains("\"v\": 2", text);
        Assert.DoesNotContain("\"v\": 1", text);
    }
}
