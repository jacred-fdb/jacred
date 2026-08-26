using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.Korsars;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Korsars;

/// <summary>
/// Live listing fixtures (login-gated). Regexes match Go cron/korsars/korsars.go.
/// Refresh: python3 scripts/dry_run_korsars_parser.py --user U --password P --refresh-fixtures
/// </summary>
public class KorsarsParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string Host = "https://korsars.pro";

    public KorsarsParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Korsars.host;
    }

    [Fact]
    public void Categories_MatchGoForumIds()
    {
        Assert.Equal("korsars", KorsarsParser.TrackerName);
        Assert.Equal(50, KorsarsParser.TopicsPerPage);
        Assert.Equal(6, KorsarsCategories.MovieIds.Count);
        Assert.Equal(12, KorsarsCategories.SerialIds.Count);
        Assert.Equal(6, KorsarsCategories.CartoonIds.Count);
        Assert.Equal(24, KorsarsCategories.Map.Count);
        Assert.Contains("282", KorsarsCategories.MovieIds);
        Assert.Contains("287", KorsarsCategories.SerialIds);
        Assert.Contains("43", KorsarsCategories.CartoonIds);
        Assert.Equal(new[] { "movie" }, KorsarsParser.CategoryTypes("282"));
        Assert.Equal(new[] { "serial" }, KorsarsParser.CategoryTypes("287"));
        Assert.Equal(new[] { "multfilm", "multserial" }, KorsarsParser.CategoryTypes("43"));
    }

    [Fact]
    public void ParseListingHtml_MovieFixture_HasInlineMagnets()
    {
        string html = FixtureLoader.Read("Korsars/listing_movie.html");
        var items = KorsarsParser.ParseListingHtml(html, "282", Host);

        _output.WriteLine($"movies={items.Count}");
        Assert.True(items.Count >= 1, $"expected >=1 movie topics, got {items.Count}");

        Assert.All(items, t =>
        {
            Assert.Equal("korsars", t.trackerName);
            Assert.Equal(new[] { "movie" }, t.types);
            Assert.StartsWith(Host + "/viewtopic.php?t=", t.url, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.magnet));
            Assert.StartsWith("magnet:?xt=urn:btih:", t.magnet, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("&amp;", t.magnet);
            Assert.True(t.sid >= 0);
            Assert.True(t.pir >= 0);
        });

        var first = items[0];
        _output.WriteLine($"first: {first.name} / {first.originalname} {first.relased} | {first.sizeName}");
        Assert.Contains("viewtopic.php?t=", first.url, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseListingHtml_SerialFixture_ParsesSeasonTitles()
    {
        string html = FixtureLoader.Read("Korsars/listing_serial.html");
        var items = KorsarsParser.ParseListingHtml(html, "287", Host);

        _output.WriteLine($"serials={items.Count}");
        Assert.True(items.Count >= 1, $"expected >=1 serial topics, got {items.Count}");

        Assert.All(items, t =>
        {
            Assert.Equal(new[] { "serial" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.StartsWith("magnet:?xt=urn:btih:", t.magnet, StringComparison.OrdinalIgnoreCase);
        });

        // At least one title should look like a season pack or named release
        Assert.Contains(items, t => !string.IsNullOrWhiteSpace(t.title));
        _output.WriteLine($"first: {items[0].name} / {items[0].originalname} {items[0].relased}");
    }

    [Fact]
    public void LastPageFromHtml_MovieFixture()
    {
        string html = FixtureLoader.Read("Korsars/listing_movie.html");
        int last = KorsarsParser.LastPageFromHtml(html);
        _output.WriteLine($"lastPage={last}");
        Assert.True(last >= 1, $"expected last page >= 1, got {last}");
    }

    [Fact]
    public void LooksLikeLoginForm_DetectsSessionExpiry()
    {
        Assert.True(KorsarsParser.LooksLikeLoginForm(
            @"<form><input name=""login_username"" /><input name=""login_password"" /></form>"));
        Assert.False(KorsarsParser.LooksLikeLoginForm(
            @"<a id=""tt-1""><b>Title</b></a><input name=""login_username"" />"));
    }

    [Theory]
    [InlineData(
        "Игра престолов / Game of Thrones / Game of Thrones [S01] (2011) WEB-DL",
        "Игра престолов", "Game of Thrones", 2011)]
    [InlineData(
        "Во все тяжкие / Breaking Bad [S01-05] (2008) BDRip",
        "Во все тяжкие", "Breaking Bad", 2008)]
    [InlineData(
        "Чернобыль [S01] (2019) WEBRip",
        "Чернобыль", "", 2019)]
    [InlineData(
        "Матрица / The Matrix / Matrix (1999) BDRemux",
        "Матрица", "Matrix", 1999)]
    [InlineData(
        "Начало / Inception (2010) BDRip 1080p",
        "Начало", "Inception", 2010)]
    [InlineData(
        "Солярис (1972) DVDRip",
        "Солярис", "", 1972)]
    public void ParseTitle_MatchesGoShape(string title, string wantName, string wantOrig, int wantYear)
    {
        var (name, original, year) = KorsarsParser.ParseTitle(title);
        Assert.Equal(wantName, name);
        Assert.Equal(wantOrig, original);
        Assert.Equal(wantYear, year);
    }

    [Fact]
    public void FirstTokenTitle_Fallback()
    {
        Assert.Equal("Something", KorsarsParser.FirstTokenTitle("Something [S01] (2020)"));
        Assert.Equal("A", KorsarsParser.FirstTokenTitle("A / B (2020)"));
    }
}
