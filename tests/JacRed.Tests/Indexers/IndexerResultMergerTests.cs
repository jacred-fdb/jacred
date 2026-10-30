using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Xunit;

namespace JacRed.Tests.Indexers;

public class IndexerResultMergerTests
{
    const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

    static Result Item(int seeders, params string[] voices) => new()
    {
        MagnetUri = Magnet,
        Seeders = seeders,
        info = new TorrentInfo { voices = new HashSet<string>(voices) },
    };

    [Fact]
    public void MergeAndSort_DoesNotMutateInputs()
    {
        // Batches may be result lists shared across requests (IMemoryCache with evercache.validHour: 0).
        var first = Item(1, "LostFilm");
        var second = Item(5, "HDRezka");

        var merged = Assert.Single(IndexerResultMerger.MergeAndSort(new[] { first }, new[] { second }));

        Assert.Equal(new[] { "HDRezka", "LostFilm" }, merged.info.voices.OrderBy(v => v));
        Assert.Equal(5, merged.Seeders);
        Assert.Equal(new[] { "LostFilm" }, first.info.voices);
        Assert.Equal(1, first.Seeders);
        Assert.Equal(new[] { "HDRezka" }, second.info.voices);
    }

    static Result Seeded(string tracker, int seeders, int peers) => new()
    {
        Tracker = tracker,
        MagnetUri = Magnet,
        Seeders = seeders,
        Peers = peers,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergeAndSort_ReliableSeedersWinOverInflated(bool reversed)
    {
        // A v1 record from a tracker with inflated counts vs an already merged Jackett result.
        var v1 = new[] { Seeded("korsars", 231, 50) };
        var merged = new[] { Seeded("korsars, kinozal", 4, 2) };

        var result = Assert.Single(reversed
            ? IndexerResultMerger.MergeAndSort(merged, v1)
            : IndexerResultMerger.MergeAndSort(v1, merged));

        Assert.Equal(4, result.Seeders);
        Assert.Equal(2, result.Peers);
    }

    [Fact]
    public void MergeAndSort_KeepsSeedersWhenOnlyUnreliableTrackers()
    {
        var result = Assert.Single(IndexerResultMerger.MergeAndSort(
            new[] { Seeded("selezen", 104, 10) }, new[] { Seeded("korsars", 231, 50) }));

        Assert.Equal(231, result.Seeders);
    }

    [Fact]
    public void MergeAndSort_TakesMaxSeedersOfReliableTrackers()
    {
        var result = Assert.Single(IndexerResultMerger.MergeAndSort(
            new[] { Seeded("rutor", 1, 3) }, new[] { Seeded("kinozal, rutor", 5, 1) }));

        Assert.Equal(5, result.Seeders);
        Assert.Equal(3, result.Peers);
    }
}
