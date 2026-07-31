using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.Mazepa;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Mazepa;

/// <summary>
/// Regression tests for Mazepa browse parsing against live-shaped HTML
/// (dl.php downloads, no listing magnets, Ukrainian relative dates).
/// </summary>
public class MazepaParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string Host = "https://mazepa.to";

    public MazepaParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Mazepa.host;
    }

    [Fact]
    public void ParseTorrentsFromCategoryPage_Fixture_YieldsDownloadIdsWithoutMagnets()
    {
        string html = FixtureLoader.Read("Mazepa/forum_41.html");
        var torrents = MazepaParser.ParseTorrentsFromCategoryPage(html, new[] { "multfilm" }, Host);

        _output.WriteLine($"parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  {t.downloadId} | {t.sizeName} | {t.title}");

        Assert.True(torrents.Count >= 45, $"expected >=45 torrents, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("mazepa", t.trackerName);
            Assert.Equal(new[] { "multfilm" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(Host + "/viewtopic.php?t=", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.downloadId));
            Assert.True(t.downloadId.All(char.IsDigit));
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.Contains("GB", t.sizeName, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(t.magnet));
            Assert.NotEqual(default, t.createTime);
            Assert.True(t.sid >= 0);
            Assert.True(t.pir >= 0);
        });

        MazepaDetails first = Assert.Single(torrents, t => t.url.EndsWith("t=101801", StringComparison.Ordinal));
        Assert.Equal("101966", first.downloadId);
        Assert.Equal("3.99 GB", first.sizeName);
        Assert.Contains("Голос океану", first.title, StringComparison.Ordinal);
        Assert.Equal(1080, first.quality);
    }

    [Fact]
    public void ParseTorrentsFromCategoryPage_EmptyHtml_ReturnsEmpty()
    {
        Assert.Empty(MazepaParser.ParseTorrentsFromCategoryPage("", new[] { "multfilm" }, Host));
        Assert.Empty(MazepaParser.ParseTorrentsFromCategoryPage("<html></html>", new[] { "multfilm" }, Host));
    }
}

public class MazepaParserDateTests
{
    [Theory]
    [InlineData("Сьогодні 12:21")]
    [InlineData("Вчора 18:05")]
    public void ParseMazepaDate_RelativeUkrainian_Succeeds(string text)
    {
        DateTime dt = MazepaParser.ParseMazepaDate(text);
        Assert.NotEqual(default, dt);
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
    }

    [Fact]
    public void ParseMazepaDate_Today_UsesUtcToday()
    {
        DateTime dt = MazepaParser.ParseMazepaDate("Сьогодні 12:21");
        Assert.Equal(DateTime.UtcNow.Date, dt.Date);
        Assert.Equal(12, dt.Hour);
        Assert.Equal(21, dt.Minute);
    }

    [Fact]
    public void ParseMazepaDate_Yesterday_UsesUtcYesterday()
    {
        DateTime dt = MazepaParser.ParseMazepaDate("Вчора 18:05");
        Assert.Equal(DateTime.UtcNow.Date.AddDays(-1), dt.Date);
        Assert.Equal(18, dt.Hour);
        Assert.Equal(5, dt.Minute);
    }

    [Theory]
    [InlineData("4 Лис 2025, 13:00", 2025, 11, 4, 13, 0)]
    [InlineData("18 Жов 2025, 11:47", 2025, 10, 18, 11, 47)]
    [InlineData("30 Вер 2025, 14:23", 2025, 9, 30, 14, 23)]
    public void ParseMazepaDate_AbsoluteUkrainian_Succeeds(string text, int year, int month, int day, int hour, int minute)
    {
        DateTime dt = MazepaParser.ParseMazepaDate(text);
        Assert.Equal(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc), dt);
    }

    [Fact]
    public void ParseMazepaDate_Empty_ReturnsDefault()
    {
        Assert.Equal(default, MazepaParser.ParseMazepaDate(null));
        Assert.Equal(default, MazepaParser.ParseMazepaDate(""));
        Assert.Equal(default, MazepaParser.ParseMazepaDate("not a date"));
    }
}
