using System.Linq;
using JacRed.Infrastructure.Trackers.Rutracker;
using Xunit;

namespace JacRed.Tests.Rutracker;

public class RutrackerCategoriesTests
{
    [Fact]
    public void Map_HasExpectedCounts()
    {
        Assert.Equal(232, RutrackerCategories.Map.Count);
        Assert.Equal(84, RutrackerCategories.QuickParseIds.Count());
        Assert.Equal(RutrackerCategories.Map.Count, RutrackerCategories.Ids.Distinct().Count());
        Assert.True(RutrackerCategories.QuickParseIds.All(id => RutrackerCategories.Map.ContainsKey(id)));
    }

    /// <summary>
    /// Разделы, добавленные 15.08.2026, и почему их отсутствие било по выдаче.
    ///
    /// У rutracker раздел может иметь и подразделы, и собственные темы, поэтому
    /// обход родителя НЕ заменяет обход детей. Так «Дом дракона» S3 в 2160p
    /// (t=6873874) не попадал в базу вовсе: он лежит в 1171, а в списке был
    /// только родительский 119, где своих тем два десятка.
    /// </summary>
    [Theory]
    [InlineData("1171")]   // Новинки и сериалы в стадии показа (UHD Video)
    [InlineData("2366")]   // Зарубежные сериалы (HD Video)
    [InlineData("189")]    // Зарубежные сериалы
    [InlineData("2100")]   // Азиатские сериалы
    [InlineData("812")]    // Русские сериалы (UHD Video)
    [InlineData("718")]    // UHD Video
    [InlineData("1106")]   // Онгоинги (HD Video)
    public void FormerlyMissingSections_AreInQuickParse(string id)
    {
        Assert.True(RutrackerCategories.Map.ContainsKey(id));
        Assert.Contains(id, RutrackerCategories.QuickParseIds);
    }

    [Fact]
    public void Map_EveryId_HasTypesAndTitleKind()
    {
        Assert.All(RutrackerCategories.Map, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Key));
            Assert.NotNull(kv.Value.Types);
            Assert.NotEmpty(kv.Value.Types);
            Assert.All(kv.Value.Types, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        });
    }

    [Theory]
    [InlineData("1392")]
    [InlineData("2475")]
    [InlineData("2493")]
    [InlineData("2113")]
    [InlineData("2482")]
    public void FormerSportOrphans_AreTypedSport_AndNotQuickParse(string id)
    {
        Assert.True(RutrackerCategories.Map.TryGetValue(id, out var meta));
        Assert.Equal(new[] { "sport" }, meta.Types);
        Assert.Equal(RutrackerTitleKind.NonStandard, meta.TitleKind);
        Assert.False(meta.QuickParse);
    }

    [Theory]
    [InlineData("1950", "movie", RutrackerTitleKind.Movie, true)]
    [InlineData("842", "serial", RutrackerTitleKind.Serial, true)]
    [InlineData("1105", "anime", RutrackerTitleKind.NonStandard, true)]
    [InlineData("709", "documovie", RutrackerTitleKind.Movie, false)]
    [InlineData("24", "tvshow", RutrackerTitleKind.NonStandard, false)]
    [InlineData("915", "serial", RutrackerTitleKind.NonStandard, true)]
    public void Map_SampleEntries_MatchExpected(string id, string type, RutrackerTitleKind kind, bool quick)
    {
        Assert.True(RutrackerCategories.Map.TryGetValue(id, out var meta));
        Assert.Equal(new[] { type }, meta.Types);
        Assert.Equal(kind, meta.TitleKind);
        Assert.Equal(quick, meta.QuickParse);
    }

    [Fact]
    public void DocSerial_HasDocuserialAndDocumovie()
    {
        Assert.True(RutrackerCategories.Map.TryGetValue("46", out var meta));
        Assert.Equal(new[] { "docuserial", "documovie" }, meta.Types);
        Assert.Equal(RutrackerTitleKind.NonStandard, meta.TitleKind);
        Assert.False(meta.QuickParse);
    }

    [Fact]
    public void Sport_IsNeverInQuickParse()
    {
        var sports = RutrackerCategories.Map.Where(kv => kv.Value.Types.SequenceEqual(new[] { "sport" })).ToList();
        Assert.True(sports.Count >= 95);
        Assert.All(sports, kv =>
        {
            Assert.False(kv.Value.QuickParse);
            Assert.Equal(RutrackerTitleKind.NonStandard, kv.Value.TitleKind);
        });
    }
}
