using System.Linq;
using System.Text.Json;
using JacRed.Infrastructure.Trackers.Rutracker;
using Xunit;

namespace JacRed.Tests.Rutracker;

public class RutrackerCategoriesTests
{
    [Fact]
    public void Map_HasExpectedCounts()
    {
        Assert.Equal(246, RutrackerCategories.Map.Count);
        Assert.Equal(98, RutrackerCategories.QuickParseIds.Count());
        Assert.Equal(RutrackerCategories.Map.Count, RutrackerCategories.Ids.Distinct().Count());
        Assert.True(RutrackerCategories.QuickParseIds.All(id => RutrackerCategories.Map.ContainsKey(id)));
    }

    /// <summary>
    /// Разделы, которых не было в карте, и почему обход родителя не спасает.
    ///
    /// У rutracker раздел может иметь и подразделы, и собственные темы, поэтому
    /// обход родителя НЕ заменяет обход детей. «Дом дракона» S3 в 2160p
    /// (t=6873874) лежит в 1171, а в списке был только родитель 119.
    /// Silo S2 UHD (t=6601495) уехала в 1669 «Сериалы США и Канады (UHD Video)»
    /// при том же родителе 119.
    /// </summary>
    [Theory]
    [InlineData("1171")]   // Новинки и сериалы в стадии показа (UHD Video)
    [InlineData("2366")]   // Зарубежные сериалы (HD Video)
    [InlineData("189")]    // Зарубежные сериалы
    [InlineData("2100")]   // Азиатские сериалы
    [InlineData("812")]    // Русские сериалы (UHD Video)
    [InlineData("718")]    // UHD Video
    [InlineData("1106")]   // Онгоинги (HD Video)
    [InlineData("1669")]   // Сериалы США и Канады (UHD Video) — Silo t=6601495
    [InlineData("2393")]   // Сериалы Великобритании и Ирландии (UHD Video)
    [InlineData("625")]    // Европейские сериалы (UHD Video)
    [InlineData("1949")]   // Сериалы Австралии и Новой Зеландии (UHD Video)
    [InlineData("173")]    // Сериалы совместного производства (UHD Video)
    [InlineData("820")]    // Азиатские сериалы (UHD Video)
    [InlineData("1242")]   // Корейские сериалы (HD Video)
    [InlineData("717")]    // Китайские сериалы
    [InlineData("2412")]   // Сериалы Таиланда, Индонезии, Сингапура
    [InlineData("1463")]   // Сериалы Африки / Ближнего и Среднего Востока (HD Video)
    [InlineData("84")]     // Мультфильмы (UHD Video)
    [InlineData("498")]    // Мультсериалы (UHD Video)
    [InlineData("272")]    // Азиатские фильмы (UHD Video)
    [InlineData("775")]    // Классика мирового кинематографа (UHD Video)
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
    [InlineData("1669", "serial", RutrackerTitleKind.Serial, true)]
    [InlineData("820", "serial", RutrackerTitleKind.NonStandard, true)]
    [InlineData("84", "multfilm", RutrackerTitleKind.Movie, true)]
    [InlineData("498", "multserial", RutrackerTitleKind.Serial, true)]
    [InlineData("272", "movie", RutrackerTitleKind.Movie, true)]
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
    public void ForumTreeSnapshot_P0Leaves_AreInQuickParse()
    {
        string json = FixtureLoader.Read("Rutracker/forum_tree_snapshot.json");
        using var doc = JsonDocument.Parse(json);
        var p0 = doc.RootElement.GetProperty("forums")
            .EnumerateArray()
            .Where(f => f.GetProperty("priority").GetString() == "p0")
            .Select(f => f.GetProperty("id").GetString())
            .Distinct()
            .ToList();

        Assert.Equal(14, p0.Count);
        Assert.Contains("1669", p0);
        Assert.All(p0, id =>
        {
            Assert.True(RutrackerCategories.Map.ContainsKey(id), $"P0 forum {id} missing from map");
            Assert.Contains(id, RutrackerCategories.QuickParseIds);
        });
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
