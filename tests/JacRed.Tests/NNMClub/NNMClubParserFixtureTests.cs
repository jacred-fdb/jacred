using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.NNMClub;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.NNMClub;

/// <summary>
/// Regression tests against captured NNMClub portal HTML.
/// Refresh: python3 scripts/dry_run_nnmclub_parser.py --refresh-fixtures
/// </summary>
public class NNMClubParserFixtureTests
{
    readonly ITestOutputHelper _output;

    public NNMClubParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.NNMClub.host;
    }

    public static IEnumerable<object[]> FixtureCases()
    {
        foreach (var kv in NNMClubCategories.Map.OrderBy(x => int.Parse(x.Key)))
            yield return new object[] { kv.Key, $"portal_c{kv.Key}.html", kv.Value.Types };
    }

    [Fact]
    public void FixtureCases_CoverEntireCategoryMap()
    {
        Assert.Equal(NNMClubCategories.Map.Count, FixtureCases().Count());
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void ParseTorrentsFromPage_Fixture_YieldsTypedTorrents(string cat, string fixtureFile, string[] expectedTypes)
    {
        string html = FixtureLoader.Read($"NNMClub/{fixtureFile}");
        List<TorrentBaseDetails> torrents = NNMClubParser.ParseTorrentsFromPage(html, cat);

        _output.WriteLine($"cat={cat} fixture={fixtureFile} parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  name={t.name} | orig={t.originalname} | year={t.relased} | {t.title}");

        // Cat 7 filters non-cartoon rows; others should fill most of the portal page.
        int minExpected = cat == "7" ? 1 : 10;
        Assert.True(torrents.Count >= minExpected, $"expected >={minExpected} torrents for cat {cat}, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("nnmclub", t.trackerName);
            Assert.Equal(expectedTypes, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.Contains("/forum/viewtopic.php?t=", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.magnet));
            Assert.StartsWith("magnet:", t.magnet, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.NotEqual(default, t.createTime);
            Assert.DoesNotContain("трейлер", t.title, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void SportFixture_TypedAsSport()
    {
        string html = FixtureLoader.Read("NNMClub/portal_c24.html");
        var torrents = NNMClubParser.ParseTorrentsFromPage(html, "24");

        Assert.True(torrents.Count >= 10);
        Assert.All(torrents, t =>
        {
            Assert.Equal(new[] { "sport" }, t.types);
            Assert.DoesNotContain("movie", t.types);
        });
    }

    [Fact]
    public void ParseTorrentsFromPage_UnknownCategory_ReturnsEmpty()
    {
        string html = FixtureLoader.Read("NNMClub/portal_c10.html");
        Assert.Empty(NNMClubParser.ParseTorrentsFromPage(html, "2"));
    }

    [Fact]
    public void DryRun_AllFixtures_ReportParseRates()
    {
        foreach (object[] row in FixtureCases())
        {
            string cat = (string)row[0];
            string file = (string)row[1];
            string[] types = (string[])row[2];
            string html = FixtureLoader.Read($"NNMClub/{file}");
            var torrents = NNMClubParser.ParseTorrentsFromPage(html, cat);
            int withYear = torrents.Count(t => t.relased > 0);
            int withOriginal = torrents.Count(t => !string.IsNullOrWhiteSpace(t.originalname));
            _output.WriteLine(
                $"DRY-RUN cat={cat,-3} types=[{string.Join(",", types)}] " +
                $"parsed={torrents.Count,3} withYear={withYear,3} withOriginal={withOriginal,3}");
            int minExpected = cat == "7" ? 1 : 10;
            Assert.True(torrents.Count >= minExpected, $"dry-run cat {cat}: low yield {torrents.Count}");
        }
    }
}
