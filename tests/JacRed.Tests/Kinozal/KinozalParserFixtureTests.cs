using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.Kinozal;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Kinozal;

/// <summary>
/// Regression tests against captured browse.php HTML for every mapped category.
/// Refresh fixtures:
///   python3 scripts/dry_run_kinozal_parser.py --user U --password P --refresh-fixtures
/// </summary>
public class KinozalParserFixtureTests
{
    readonly ITestOutputHelper _output;

    public KinozalParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Kinozal.host;
    }

    public static IEnumerable<object[]> FixtureCases()
    {
        foreach (var kv in KinozalCategories.Map.OrderBy(x => int.Parse(x.Key)))
            yield return new object[] { kv.Key, $"browse_c{kv.Key}.html", kv.Value.Types };
    }

    [Fact]
    public void FixtureCases_CoverEntireCategoryMap()
    {
        Assert.Equal(KinozalCategories.Map.Count, FixtureCases().Count());
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void ParseTorrentsFromPage_Fixture_YieldsTypedTorrents(string cat, string fixtureFile, string[] expectedTypes)
    {
        string html = FixtureLoader.Read($"Kinozal/{fixtureFile}");
        List<TorrentDetails> torrents = KinozalParser.ParseTorrentsFromPage(html, cat);

        _output.WriteLine($"cat={cat} fixture={fixtureFile} parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  name={t.name} | orig={t.originalname} | year={t.relased} | {t.title}");

        // Browse pages are typically 50 rows; allow some title-shape misses but require strong yield.
        Assert.True(torrents.Count >= 40, $"expected >=40 torrents for cat {cat}, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("kinozal", t.trackerName);
            Assert.Equal(expectedTypes, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(AppInit.conf.Kinozal.host.TrimEnd('/') + "/", t.url, StringComparison.Ordinal);
            Assert.Contains("details.php?id=", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.NotEqual(default, t.createTime);
            Assert.True(t.sid >= 0);
            Assert.True(t.pir >= 0);
        });
    }

    [Fact]
    public void ParseTorrentsFromPage_Fixture_C50_ParsesTerabyteSizes()
    {
        string html = FixtureLoader.Read("Kinozal/browse_c50.html");
        List<TorrentDetails> torrents = KinozalParser.ParseTorrentsFromPage(html, "50");

        TorrentDetails tb = Assert.Single(torrents, t => t.sizeName.Contains("ТБ", StringComparison.Ordinal));
        Assert.Equal("2.278 ТБ", tb.sizeName);
        Assert.Contains("МастерШеф", tb.title, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseTorrentsFromPage_UnknownCategory_ReturnsEmpty()
    {
        string html = FixtureLoader.Read("Kinozal/browse_c8.html");
        Assert.Empty(KinozalParser.ParseTorrentsFromPage(html, "9999"));
    }

    [Fact]
    public void ParseTorrentsFromPage_EmptyHtml_ReturnsEmpty()
    {
        Assert.Empty(KinozalParser.ParseTorrentsFromPage("", "8"));
        Assert.Empty(KinozalParser.ParseTorrentsFromPage("<html></html>", "8"));
    }

    [Fact]
    public void ParseInfoHash_FlareSolverrFragment_ReturnsLabeledHash()
    {
        const string html = "<html><head></head><body><ul><li>Инфо хеш: 7C4FCE77B05BC2711C8445C0B1E47011CF4214CE</li>"
            + "<li>Размер части торрента: 1 МБ</li></ul></body></html>";

        Assert.Equal("7C4FCE77B05BC2711C8445C0B1E47011CF4214CE", KinozalParser.ParseInfoHash(html));
    }

    [Fact]
    public void ParseInfoHash_BrowseSizedHtml_DoesNotUseLooseHex()
    {
        string html = new string('x', 9000) + "0123456789abcdef0123456789abcdef01234567";
        Assert.Null(KinozalParser.ParseInfoHash(html));
    }

    [Fact]
    public void ParseInfoHash_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(KinozalParser.ParseInfoHash(null));
        Assert.Null(KinozalParser.ParseInfoHash(""));
        Assert.Null(KinozalParser.ParseInfoHash("Торрент файл не найден."));
    }

    [Fact]
    public void IsTransientBrowseFailure_NullAndNginx503()
    {
        Assert.True(KinozalParser.IsTransientBrowseFailure(null));
        Assert.True(KinozalParser.IsTransientBrowseFailure(""));
        Assert.True(KinozalParser.IsTransientBrowseFailure(
            "<html><head><title>503 Service Temporarily Unavailable</title></head></html>"));
        Assert.True(KinozalParser.IsTransientBrowseFailure("<title>Just a moment...</title>"));
        Assert.False(KinozalParser.IsTransientBrowseFailure(FixtureLoader.Read("Kinozal/browse_c22.html")));
    }

    [Fact]
    public void IsLoginWall_AndLoggedIn_FromFixture()
    {
        string listing = FixtureLoader.Read("Kinozal/browse_c22.html");
        Assert.True(KinozalParser.IsLoggedIn(listing));
        Assert.False(KinozalParser.IsLoginWall(listing));
        Assert.True(KinozalParser.IsLoginWall("<form action=\"/takelogin.php\"><input name=\"username\">"));
        Assert.False(KinozalParser.IsLoginWall("<title>503 Service Temporarily Unavailable</title>"));
    }

    [Fact]
    public void ShouldMarkPageDone_EmptyOrFullyResolved()
    {
        Assert.True(KinozalParser.ShouldMarkPageDone(0, 0));
        Assert.True(KinozalParser.ShouldMarkPageDone(10, 10));
        Assert.False(KinozalParser.ShouldMarkPageDone(10, 0));
        Assert.False(KinozalParser.ShouldMarkPageDone(10, 9));
    }

    [Fact]
    public void DryRun_AllFixtures_ReportParseRates()
    {
        foreach (object[] row in FixtureCases())
        {
            string cat = (string)row[0];
            string file = (string)row[1];
            string[] types = (string[])row[2];
            string html = FixtureLoader.Read($"Kinozal/{file}");
            var torrents = KinozalParser.ParseTorrentsFromPage(html, cat);
            int withYear = torrents.Count(t => t.relased > 0);
            int withOriginal = torrents.Count(t => !string.IsNullOrWhiteSpace(t.originalname));
            _output.WriteLine(
                $"DRY-RUN cat={cat,-3} types=[{string.Join(",", types)}] " +
                $"parsed={torrents.Count,2} withYear={withYear,2} withOriginal={withOriginal,2}");
            Assert.True(torrents.Count >= 40, $"dry-run cat {cat}: low yield {torrents.Count}");
        }
    }
}
