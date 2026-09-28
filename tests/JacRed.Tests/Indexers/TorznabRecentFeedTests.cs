using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Xunit;

namespace JacRed.Tests.Indexers;

public class TorznabRecentFeedTests
{
    [Fact]
    public void Select_NewestCreateTimeWins()
    {
        var older = Item("old", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), 2000);
        var newer = Item("new", new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), 2000);

        var page = TorznabRecentFeed.Select(new[] { older, newer }, null, null, 0, null, null, 10, 0);

        Assert.Equal(new[] { "new", "old" }, page.Select(i => i.Title));
    }

    [Fact]
    public void Select_Cat5000_DropsMoviesAndEmptyCategory()
    {
        var movie = Item("movie", DateTime.UtcNow, 2000);
        var serial = Item("serial", DateTime.UtcNow.AddMinutes(-1), 5000);
        var sport = new Result
        {
            Title = "sport",
            PublishDate = DateTime.UtcNow.AddMinutes(1),
            Category = new HashSet<int>()
        };

        var page = TorznabRecentFeed.Select(new[] { movie, serial, sport }, "5000", null, 0, null, null, 10, 0);

        Assert.Equal("serial", Assert.Single(page).Title);
    }

    [Fact]
    public void Select_Cat2000And5000_KeepsBoth()
    {
        var movie = Item("movie", DateTime.UtcNow, 2000);
        var serial = Item("serial", DateTime.UtcNow.AddMinutes(-1), 5000);

        var page = TorznabRecentFeed.Select(new[] { movie, serial }, "2000,5000", null, 0, null, null, 10, 0);

        Assert.Equal(2, page.Count);
    }

    [Fact]
    public void Select_DefaultPageSize100()
    {
        var items = Enumerable.Range(0, 150).Select(i =>
            Item("t" + i, DateTime.UtcNow.AddMinutes(-i), 2000));

        var page = TorznabRecentFeed.Select(items, null, null, 0, null, null, null, 0);

        Assert.Equal(IndexerResultFilters.DefaultLimit, page.Count);
        Assert.Equal("t0", page[0].Title);
    }

    static Result Item(string title, DateTime published, int cat) => new()
    {
        Title = title,
        PublishDate = published,
        Category = new HashSet<int> { cat }
    };
}
