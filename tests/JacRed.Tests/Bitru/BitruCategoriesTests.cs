using System.Linq;
using JacRed.Infrastructure.Trackers.Bitru;
using Xunit;

namespace JacRed.Tests.Bitru;

public class BitruCategoriesTests
{
    [Fact]
    public void RequestCategories_AreMovieSerialVideo()
    {
        Assert.Equal(new[] { "movie", "serial", "video" }, BitruCategories.RequestCategories);
    }

    [Theory]
    [InlineData("movie", null, "movie")]
    [InlineData("serial", null, "serial")]
    [InlineData("Movie", null, "movie")]
    public void TryGetTypes_MovieAndSerial(string category, string[] subsections, string expectedType)
    {
        Assert.True(BitruCategories.TryGetTypes(category, subsections, out var types));
        Assert.Equal(new[] { expectedType }, types);
    }

    [Theory]
    [InlineData("Документальный", "documovie")]
    [InlineData("Научный", "documovie")]
    [InlineData("Исторический", "documovie")]
    [InlineData("Биография", "documovie")]
    [InlineData("Спорт", "sport")]
    [InlineData("Шоу", "tvshow")]
    [InlineData("Клипы", "tvshow")]
    [InlineData("Концерт", "tvshow")]
    public void TryGetTypes_VideoSubsections(string subsection, string expectedType)
    {
        Assert.True(BitruCategories.TryGetTypes("video", new[] { subsection }, out var types));
        Assert.Equal(new[] { expectedType }, types);
    }

    [Theory]
    [InlineData("Трейлер")]
    [InlineData("Эротика")]
    [InlineData("Уроки")]
    [InlineData("Детское")]
    [InlineData("Неизвестный")]
    public void TryGetTypes_VideoDroppedSubsections(string subsection)
    {
        Assert.False(BitruCategories.TryGetTypes("video", new[] { subsection }, out _));
    }

    [Theory]
    [InlineData("music")]
    [InlineData("game")]
    [InlineData("soft")]
    [InlineData("xxx")]
    public void TryGetTypes_NonVideo_ReturnsFalse(string category)
    {
        Assert.False(BitruCategories.TryGetTypes(category, null, out _));
        Assert.Contains(category, BitruCategories.NonVideoIds);
    }

    [Fact]
    public void Video_FirstMatchingSubsection_Wins()
    {
        Assert.True(BitruCategories.TryGetTypes("video", new[] { "Трейлер", "Спорт" }, out var types));
        Assert.Equal(new[] { "sport" }, types);
    }
}
