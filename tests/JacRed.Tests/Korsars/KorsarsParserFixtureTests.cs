using System;
using JacRed.Infrastructure.Trackers.Korsars;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Korsars;

/// <summary>
/// Synthetic fixtures (login-gated site). Regexes match Go cron/korsars/korsars.go.
/// Refresh live: python3 scripts/dry_run_korsars_parser.py --user U --password P --refresh-fixtures
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
        Assert.Equal(3, items.Count);

        Assert.Equal($"{Host}/viewtopic.php?t=12345", items[0].url);
        Assert.Equal("Начало", items[0].name);
        Assert.Equal("Inception", items[0].originalname);
        Assert.Equal(2010, items[0].relased);
        Assert.Equal(10, items[0].sid);
        Assert.Equal(2, items[0].pir);
        Assert.Equal("1.50 GB", items[0].sizeName);
        Assert.StartsWith("magnet:?xt=urn:btih:", items[0].magnet);
        Assert.DoesNotContain("&amp;", items[0].magnet);
        Assert.Equal(new[] { "movie" }, items[0].types);
        Assert.Equal(new DateTime(2024, 6, 15, 9, 30, 0, DateTimeKind.Utc), items[0].createTime);

        Assert.Equal("Матрица", items[1].name);
        Assert.Equal("Matrix", items[1].originalname);
        Assert.Equal(1999, items[1].relased);

        Assert.Equal("Солярис", items[2].name);
        Assert.Equal("", items[2].originalname);
        Assert.Equal(1972, items[2].relased);
    }

    [Fact]
    public void ParseListingHtml_SerialFixture_ParsesSeasonTitles()
    {
        string html = FixtureLoader.Read("Korsars/listing_serial.html");
        var items = KorsarsParser.ParseListingHtml(html, "287", Host);

        _output.WriteLine($"serials={items.Count}");
        Assert.Equal(3, items.Count);

        Assert.Equal("Игра престолов", items[0].name);
        Assert.Equal("Game of Thrones", items[0].originalname);
        Assert.Equal(2011, items[0].relased);
        Assert.Equal(new[] { "serial" }, items[0].types);

        Assert.Equal("Во все тяжкие", items[1].name);
        Assert.Equal("Breaking Bad", items[1].originalname);
        Assert.Equal(2008, items[1].relased);

        Assert.Equal("Чернобыль", items[2].name);
        Assert.Equal("", items[2].originalname);
        Assert.Equal(2019, items[2].relased);
    }

    [Fact]
    public void LastPageFromHtml_MovieFixture()
    {
        string html = FixtureLoader.Read("Korsars/listing_movie.html");
        Assert.Equal(2, KorsarsParser.LastPageFromHtml(html));
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
