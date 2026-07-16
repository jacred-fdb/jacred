using System.Linq;
using JacRed.Infrastructure.Trackers.TorrentBy;
using Xunit;

namespace JacRed.Tests.TorrentBy;

public class TorrentByCategoriesTests
{
    [Fact]
    public void Map_HasExpectedCount_AndIncludesSeries()
    {
        Assert.Equal(9, TorrentByCategories.Map.Count);
        Assert.True(TorrentByCategories.Map.ContainsKey("series"));
        Assert.Equal(TorrentByCategories.Map.Count, TorrentByCategories.Ids.Distinct().Count());
    }

    [Theory]
    [InlineData("films", "movie")]
    [InlineData("movies", "movie")]
    [InlineData("serials", "serial")]
    [InlineData("series", "serial")]
    [InlineData("tv", "tvshow")]
    [InlineData("humor", "tvshow")]
    [InlineData("anime", "anime")]
    [InlineData("sport", "sport")]
    public void Map_Types_MatchExpected(string cat, params string[] expectedTypes)
    {
        Assert.True(TorrentByCategories.Map.TryGetValue(cat, out var meta));
        Assert.Equal(expectedTypes, meta.Types);
    }

    [Fact]
    public void Cartoons_HasMultTypes()
    {
        Assert.True(TorrentByCategories.Map.TryGetValue("cartoons", out var meta));
        Assert.Equal(new[] { "multfilm", "multserial" }, meta.Types);
    }

    [Fact]
    public void SportCategory_HasSportType_NotMovie()
    {
        Assert.True(TorrentByCategories.Map.TryGetValue("sport", out var meta));
        Assert.Equal(new[] { "sport" }, meta.Types);
        Assert.DoesNotContain("movie", meta.Types);
        Assert.Equal(TorrentByTitleKind.Sport, meta.TitleKind);
    }

    [Theory]
    [InlineData("films", TorrentByTitleKind.FilmsForeign)]
    [InlineData("movies", TorrentByTitleKind.FilmsRu)]
    [InlineData("serials", TorrentByTitleKind.SerialForeign)]
    [InlineData("series", TorrentByTitleKind.SerialRu)]
    [InlineData("tv", TorrentByTitleKind.ShowLike)]
    public void Map_TitleKind_MatchExpected(string cat, TorrentByTitleKind kind)
    {
        Assert.True(TorrentByCategories.Map.TryGetValue(cat, out var meta));
        Assert.Equal(kind, meta.TitleKind);
    }

    [Fact]
    public void Map_DoesNotInclude_NonVideo()
    {
        foreach (var id in new[] { "music", "games", "books", "software", "soft", "other", "belarus" })
            Assert.False(TorrentByCategories.Map.ContainsKey(id), $"unexpected cat {id}");
    }
}
