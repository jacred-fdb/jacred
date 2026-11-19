using System;
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

    [Fact]
    public void MergeAndSort_SubsTakenFromEitherBatch()
    {
        var jackett = Item(5);
        var v1 = Item(1);
        v1.subs = new List<string> { "English" };

        var merged = Assert.Single(IndexerResultMerger.MergeAndSort(new[] { jackett }, new[] { v1 }));

        Assert.Equal(new[] { "English" }, merged.subs);
        Assert.Null(jackett.subs);
    }

    static readonly DateTime Renamed = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    // A renamed topic: the old record stays in its old bucket and under the old domain.
    static Result Copy(string tracker, string details, string title, DateTime updateTime, int seeders = 5, params string[] voices) => new()
    {
        Tracker = tracker,
        Details = details,
        Title = title,
        MagnetUri = Magnet,
        Seeders = seeders,
        UpdateTime = updateTime,
        info = new TorrentInfo { name = title, voices = new HashSet<string>(voices) },
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergeAndSort_TakesRecordOfLaterCopyOnSameTracker(bool reversed)
    {
        var old = Copy("rutracker", "https://rutracker.net/forum/viewtopic.php?t=1", "Old title [TV+Special]", Renamed, 8, "AniDub");
        var fresh = Copy("rutracker", "https://rutracker.org/forum/viewtopic.php?t=1", "New title (TV-1) [TV+Special]", Renamed.AddMonths(9), 6);

        var result = Assert.Single(reversed
            ? IndexerResultMerger.MergeAndSort(new[] { fresh }, new[] { old })
            : IndexerResultMerger.MergeAndSort(new[] { old }, new[] { fresh }));

        Assert.Equal("New title (TV-1) [TV+Special]", result.Title);
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=1", result.Details);
        Assert.Equal("New title (TV-1) [TV+Special]", result.info.name);
        Assert.Equal(8, result.Seeders);
        Assert.Equal(new[] { "AniDub" }, result.info.voices);
        Assert.Equal("Old title [TV+Special]", old.Title);
        Assert.Empty(fresh.info.voices);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    public void MergeAndSort_KeepsFirstRecordOnEqualUpdateTime(double laterSeconds)
    {
        // One sync pass writes copies microseconds apart: that order says nothing of the topic.
        var first = Copy("rutracker", "https://rutracker.net/forum/viewtopic.php?t=1", "First", Renamed);
        var second = Copy("rutracker", "https://rutracker.org/forum/viewtopic.php?t=1", "Second", Renamed.AddSeconds(laterSeconds));

        var result = Assert.Single(IndexerResultMerger.MergeAndSort(new[] { first }, new[] { second }));

        Assert.Equal("First", result.Title);
        Assert.Equal("https://rutracker.net/forum/viewtopic.php?t=1", result.Details);
    }

    [Fact]
    public void MergeAndSort_KeepsFirstRecordForLaterCopyOfOtherTracker()
    {
        // The update time of another tracker's record tells nothing about this title.
        var rutracker = Copy("rutracker", "https://rutracker.org/forum/viewtopic.php?t=1", "Rutracker title", Renamed);
        var kinozal = Copy("kinozal", "https://kinozal.tv/details.php?id=1", "Kinozal title", Renamed.AddDays(1));

        var result = Assert.Single(IndexerResultMerger.MergeAndSort(new[] { rutracker }, new[] { kinozal }));

        Assert.Equal("Rutracker title", result.Title);
        Assert.Equal("rutracker", result.Tracker);
    }
}
