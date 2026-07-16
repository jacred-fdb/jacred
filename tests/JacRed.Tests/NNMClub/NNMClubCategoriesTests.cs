using System.Linq;
using JacRed.Infrastructure.Trackers.NNMClub;
using Xunit;

namespace JacRed.Tests.NNMClub;

public class NNMClubCategoriesTests
{
    [Fact]
    public void Map_HasExpectedCount()
    {
        Assert.Equal(13, NNMClubCategories.Map.Count);
        Assert.Equal(NNMClubCategories.Map.Count, NNMClubCategories.Ids.Distinct().Count());
    }

    [Fact]
    public void Map_EveryId_HasTypesAndTitleKind()
    {
        Assert.All(NNMClubCategories.Map, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Key));
            Assert.NotNull(kv.Value.Types);
            Assert.NotEmpty(kv.Value.Types);
            Assert.All(kv.Value.Types, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        });
    }

    [Theory]
    [InlineData("10", "movie", NNMClubTitleKind.ForeignCinema)]
    [InlineData("13", "movie", NNMClubTitleKind.RuMovie)]
    [InlineData("3", "serial", NNMClubTitleKind.ForeignSerial)]
    [InlineData("4", "serial", NNMClubTitleKind.RuSerial)]
    [InlineData("1", "anime", NNMClubTitleKind.Anime)]
    [InlineData("24", "sport", NNMClubTitleKind.Sport)]
    [InlineData("21", "tvshow", NNMClubTitleKind.ShowLike)]
    [InlineData("27", "tvshow", NNMClubTitleKind.ShowLike)]
    public void Map_SampleEntries_MatchExpected(string id, string type, NNMClubTitleKind kind)
    {
        Assert.True(NNMClubCategories.Map.TryGetValue(id, out var meta));
        Assert.Equal(new[] { type }, meta.Types);
        Assert.Equal(kind, meta.TitleKind);
    }

    [Fact]
    public void Docs_HasDocuserialAndDocumovie()
    {
        Assert.True(NNMClubCategories.Map.TryGetValue("22", out var meta));
        Assert.Equal(new[] { "docuserial", "documovie" }, meta.Types);
        Assert.Equal(NNMClubTitleKind.ShowLike, meta.TitleKind);
    }

    [Fact]
    public void Kids_HasMultFlags()
    {
        Assert.True(NNMClubCategories.Map.TryGetValue("7", out var meta));
        Assert.Equal(new[] { "multfilm", "multserial" }, meta.Types);
        Assert.Equal(NNMClubTitleKind.KidsMult, meta.TitleKind);
        Assert.True(meta.RequireMultInRow);
        Assert.True(meta.SkipPdfInTitle);
    }

    [Fact]
    public void SportCategory_HasSportType_NotMovie()
    {
        Assert.True(NNMClubCategories.Map.TryGetValue("24", out var meta));
        Assert.Equal(new[] { "sport" }, meta.Types);
        Assert.DoesNotContain("movie", meta.Types);
    }

    [Fact]
    public void Map_DoesNotInclude_NonVideo()
    {
        foreach (var id in NNMClubCategories.NonVideoIds)
            Assert.False(NNMClubCategories.Map.ContainsKey(id), $"unexpected cat {id}");
    }

    [Fact]
    public void NonVideoIds_IncludesEventCat28()
    {
        Assert.Contains("28", NNMClubCategories.NonVideoIds);
    }

    [Fact]
    public void PageSize_Is25()
    {
        Assert.Equal(25, NNMClubSyncService.PageSize);
    }
}
