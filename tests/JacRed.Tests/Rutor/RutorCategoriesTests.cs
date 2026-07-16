using System.Linq;
using JacRed.Infrastructure.Trackers.Rutor;
using Xunit;

namespace JacRed.Tests.Rutor;

public class RutorCategoriesTests
{
    [Fact]
    public void Map_HasExpectedCount()
    {
        Assert.Equal(11, RutorCategories.Map.Count);
        Assert.Equal(RutorCategories.Map.Count, RutorCategories.Ids.Distinct().Count());
    }

    [Fact]
    public void Map_EveryId_HasTypesAndTitleKind()
    {
        Assert.All(RutorCategories.Map, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Key));
            Assert.NotNull(kv.Value.Types);
            Assert.NotEmpty(kv.Value.Types);
            Assert.All(kv.Value.Types, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        });
    }

    [Theory]
    [InlineData("1", "movie", RutorTitleKind.ForeignMovie, false)]
    [InlineData("17", "movie", RutorTitleKind.ForeignMovie, true)]
    [InlineData("5", "movie", RutorTitleKind.RuMovie, false)]
    [InlineData("4", "serial", RutorTitleKind.ForeignSerial, false)]
    [InlineData("16", "serial", RutorTitleKind.RuSerial, false)]
    [InlineData("10", "anime", RutorTitleKind.ShowLike, false)]
    [InlineData("13", "sport", RutorTitleKind.ShowLike, false)]
    public void Map_SampleEntries_MatchExpected(string id, string type, RutorTitleKind kind, bool ukr)
    {
        Assert.True(RutorCategories.Map.TryGetValue(id, out var meta));
        Assert.Equal(new[] { type }, meta.Types);
        Assert.Equal(kind, meta.TitleKind);
        Assert.Equal(ukr, meta.RequireUkrInTitle);
    }

    [Fact]
    public void Docs_HasDocuserialAndDocumovie()
    {
        Assert.True(RutorCategories.Map.TryGetValue("12", out var meta));
        Assert.Equal(new[] { "docuserial", "documovie" }, meta.Types);
        Assert.Equal(RutorTitleKind.ShowLike, meta.TitleKind);
    }

    [Fact]
    public void Cartoons_HasMultTypes()
    {
        Assert.True(RutorCategories.Map.TryGetValue("7", out var meta));
        Assert.Equal(new[] { "multfilm", "multserial" }, meta.Types);
    }

    [Fact]
    public void SportCategory_HasSportType_NotMovie()
    {
        Assert.True(RutorCategories.Map.TryGetValue("13", out var meta));
        Assert.Equal(new[] { "sport" }, meta.Types);
        Assert.DoesNotContain("movie", meta.Types);
    }

    [Fact]
    public void OnlyCat17_RequiresUkr()
    {
        Assert.All(RutorCategories.Map, kv =>
        {
            if (kv.Key == "17")
                Assert.True(kv.Value.RequireUkrInTitle);
            else
                Assert.False(kv.Value.RequireUkrInTitle);
        });
    }

    [Fact]
    public void Map_DoesNotInclude_NonVideo()
    {
        foreach (var id in new[] { "2", "3", "8", "9", "11", "14" })
            Assert.False(RutorCategories.Map.ContainsKey(id), $"unexpected cat {id}");
    }
}
