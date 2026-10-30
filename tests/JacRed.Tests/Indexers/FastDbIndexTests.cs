using System.Collections.Concurrent;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Models;
using Xunit;

namespace JacRed.Tests.Indexers;

// FileDB is process-wide; do not replace or mutate its index alongside other tests.
[CollectionDefinition("FastDb index", DisableParallelization = true)]
public class FastDbIndexCollection { }

[Collection("FastDb index")]
public class FastDbIndexTests : IDisposable
{
    readonly List<string> _keys = new();
    readonly string _prefix = Guid.NewGuid().ToString("N");

    string Add(string name, string original = "original")
    {
        string key = $"{_prefix}{name}:{_prefix}{original}";
        _keys.Add(key);
        FileDB.SetShard(key, DateTime.UtcNow);
        return key;
    }

    public void Dispose()
    {
        foreach (string key in _keys)
            FileDB.RemoveKeyFromMasterDb(key);
    }

    [Fact]
    public void UnchangedRefresh_ReusesPublishedIndexWithoutRebuilding()
    {
        string key = Add("film");
        var index = new FastDbIndex();

        Assert.True(index.RebuildIfChanged());
        var published = index.Get();
        Assert.Contains(key, published[_prefix + "film"]);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool changed = index.RebuildIfChanged();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(changed);
        Assert.Same(published, index.Get());
        Assert.True(allocated < 1024, $"Unchanged refresh allocated {allocated} bytes");
    }

    [Fact]
    public void ExistingShardMetadataUpdate_DoesNotInvalidateSearchAliases()
    {
        string key = Add("film");
        var index = new FastDbIndex();
        index.Rebuild();
        var published = index.Get();

        FileDB.SetShard(key, DateTime.UtcNow.AddMinutes(1));

        Assert.False(index.RebuildIfChanged());
        Assert.Same(published, index.Get());
    }

    [Fact]
    public void AddedAndRemovedKeys_RefreshAliasesAndKeepOldReadersValid()
    {
        string first = Add("first", "shared");
        var index = new FastDbIndex();
        var original = index.Get();
        string second = Add("second", "shared");

        Assert.True(index.RebuildIfChanged());
        Assert.Contains(second, index.Get()[_prefix + "second"]);
        Assert.Equal(2, index.Get()[_prefix + "shared"].Count);
        Assert.False(original.ContainsKey(_prefix + "second"));

        FileDB.RemoveKeyFromMasterDb(first);

        Assert.True(index.RebuildIfChanged());
        Assert.False(index.Get().ContainsKey(_prefix + "first"));
        Assert.Equal(second, Assert.Single(index.Get()[_prefix + "shared"]));
        Assert.Contains(first, original[_prefix + "first"]);
    }

    [Fact]
    public void SameCountKeyReplacement_IsNotMistakenForAnUnchangedIndex()
    {
        string first = Add("first");
        var index = new FastDbIndex();
        index.Rebuild();

        FileDB.RemoveKeyFromMasterDb(first);
        string second = Add("second");

        Assert.True(index.RebuildIfChanged());
        Assert.False(index.Get().ContainsKey(_prefix + "first"));
        Assert.Contains(second, index.Get()[_prefix + "second"]);
    }

    [Fact]
    public void ForcedRebuild_StillReplacesAnUnchangedIndex()
    {
        Add("film");
        var index = new FastDbIndex();
        var original = index.Get();

        index.Rebuild();

        Assert.NotSame(original, index.Get());
        Assert.False(index.RebuildIfChanged());
    }

    [Fact]
    public void ReplacedSourceDictionary_RefreshesEvenWithoutAVersionChange()
    {
        Add("film");
        var originalSource = FileDB.masterDb;
        var index = new FastDbIndex();
        index.Rebuild();
        try
        {
            FileDB.masterDb = new ConcurrentDictionary<string, MasterDbShard>();

            Assert.True(index.RebuildIfChanged());
            Assert.Empty(index.Get());
        }
        finally
        {
            FileDB.masterDb = originalSource;
        }

        Assert.True(index.RebuildIfChanged());
        Assert.True(index.Get().ContainsKey(_prefix + "film"));
    }

    [Fact]
    public async Task ConcurrentMutations_EventuallyPublishAllSurvivingKeys()
    {
        var index = new FastDbIndex();
        index.Rebuild();
        string[] keys = Enumerable.Range(0, 100)
            .Select(i => $"{_prefix}film{i}:{_prefix}original{i}").ToArray();
        _keys.AddRange(keys);

        await Task.WhenAll(
            Task.Run(() => Parallel.ForEach(keys, key =>
            {
                FileDB.SetShard(key, DateTime.UtcNow);
                FileDB.RemoveKeyFromMasterDb(key);
                FileDB.SetShard(key, DateTime.UtcNow.AddMinutes(1));
            })),
            Task.Run(() =>
            {
                for (int i = 0; i < 20; i++)
                    index.RebuildIfChanged();
            }));

        index.RebuildIfChanged();
        foreach (string key in keys)
            Assert.Contains(key, index.Get()[key.Split(':')[0]]);
        Assert.False(index.RebuildIfChanged());
    }
}
