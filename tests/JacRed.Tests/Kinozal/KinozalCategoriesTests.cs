using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.Kinozal;
using Xunit;

namespace JacRed.Tests.Kinozal;

public class KinozalCategoriesTests
{
    [Fact]
    public void Map_HasExpectedCount_AndNoDuplicateIds()
    {
        Assert.Equal(25, KinozalCategories.Map.Count);
        Assert.Equal(KinozalCategories.Map.Count, KinozalCategories.Ids.Distinct().Count());
    }

    [Theory]
    [InlineData("18", "docuserial", "documovie")]
    [InlineData("37", "sport")]
    [InlineData("45", "serial")]
    [InlineData("46", "serial")]
    [InlineData("20", "anime")]
    [InlineData("49", "tvshow")]
    [InlineData("8", "movie")]
    [InlineData("21", "multfilm", "multserial")]
    [InlineData("22", "multfilm", "multserial")]
    public void Map_Types_MatchExpected(string cat, params string[] expectedTypes)
    {
        Assert.True(KinozalCategories.Map.TryGetValue(cat, out var meta));
        Assert.Equal(expectedTypes, meta.Types);
    }

    [Theory]
    [InlineData("8", KinozalTitleKind.Movie)]
    [InlineData("18", KinozalTitleKind.Movie)]
    [InlineData("37", KinozalTitleKind.Movie)] // title shape; Types are still sport
    [InlineData("45", KinozalTitleKind.SerialRu)]
    [InlineData("22", KinozalTitleKind.SerialRu)]
    [InlineData("46", KinozalTitleKind.SerialEn)]
    [InlineData("21", KinozalTitleKind.SerialEn)]
    [InlineData("20", KinozalTitleKind.SerialEn)]
    [InlineData("49", KinozalTitleKind.TvShow)]
    [InlineData("50", KinozalTitleKind.TvShow)]
    public void Map_TitleKind_MatchExpected(string cat, KinozalTitleKind kind)
    {
        Assert.True(KinozalCategories.Map.TryGetValue(cat, out var meta));
        Assert.Equal(kind, meta.TitleKind);
    }

    [Fact]
    public void Map_DoesNotInclude_MusicSoftwareTheatreOrConcert()
    {
        foreach (var id in new[] { "1", "2", "3", "4", "5", "23", "32", "38", "40", "41", "42", "48", "1001", "1002" })
            Assert.False(KinozalCategories.Map.ContainsKey(id), $"unexpected cat {id}");
    }

    [Fact]
    public void SportCategory_HasSportType_NotMovie()
    {
        Assert.True(KinozalCategories.Map.TryGetValue("37", out var meta));
        Assert.Equal(new[] { "sport" }, meta.Types);
        Assert.DoesNotContain("movie", meta.Types);
    }
}
