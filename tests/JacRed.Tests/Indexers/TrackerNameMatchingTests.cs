using System.Collections.Generic;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Xunit;

namespace JacRed.Tests.Indexers;

public class TrackerNameMatchingTests
{
    [Fact]
    public void ParseList_SplitsTrimmedDistinctIgnoreCase()
    {
        var list = TrackerNameMatching.ParseList("kinozal, RUTRACKER, kinozal,  ,toloka");

        Assert.Equal(new[] { "kinozal", "RUTRACKER", "toloka" }, list);
    }

    [Fact]
    public void Matches_EmptyAllowlist_AllowsAny()
    {
        Assert.True(TrackerNameMatching.Matches("rutracker", (IReadOnlyCollection<string>)null));
        Assert.True(TrackerNameMatching.Matches("rutracker", new List<string>()));
    }

    [Fact]
    public void Matches_MergedTrackerName_MatchesAnyPartIgnoreCase()
    {
        var allowed = TrackerNameMatching.ToAllowSet(new[] { "RUTRACKER" });

        Assert.True(TrackerNameMatching.Matches("kinozal, rutracker", allowed));
        Assert.False(TrackerNameMatching.Matches("kinozal, rutor", allowed));
        Assert.False(TrackerNameMatching.Matches(null, allowed));
    }

    [Fact]
    public void ApplyIndexerPathFilter_AddsSpecificIndexer()
    {
        var req = new IndexerSearchRequest { Trackers = new List<string> { "kinozal" } };

        TrackerNameMatching.ApplyIndexerPathFilter(req, "rutracker");

        Assert.Contains("rutracker", req.Trackers);
        Assert.Contains("kinozal", req.Trackers);
        Assert.Equal("rutracker", req.Tracker);
    }

    [Fact]
    public void ApplyIndexerPathFilter_IgnoresNumericProwlarrId()
    {
        var req = new IndexerSearchRequest();

        TrackerNameMatching.ApplyIndexerPathFilter(req, "1");

        Assert.True(req.Trackers == null || req.Trackers.Count == 0);
        Assert.Null(req.Tracker);
    }

    [Fact]
    public void ApplyIndexerPathFilter_IgnoresAll()
    {
        var req = new IndexerSearchRequest();

        TrackerNameMatching.ApplyIndexerPathFilter(req, "all");

        Assert.True(req.Trackers == null || req.Trackers.Count == 0);
        Assert.Null(req.Tracker);
    }

    [Fact]
    public void FilterByTrackers_UsesSharedMatcher()
    {
        var items = new List<Result>
        {
            new() { Tracker = "kinozal, rutracker", Title = "a" },
            new() { Tracker = "rutor", Title = "b" }
        };

        var filtered = IndexerResultFilters.FilterByTrackers(items, new List<string> { "RUTRACKER" });

        Assert.Single(filtered);
        Assert.Equal("a", filtered[0].Title);
    }
}
