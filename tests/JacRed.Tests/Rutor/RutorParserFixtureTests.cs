using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.Rutor;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Rutor;

/// <summary>
/// Regression tests against captured Rutor HTML.
/// Refresh: python3 scripts/dry_run_rutor_parser.py --refresh-fixtures
/// </summary>
public class RutorParserFixtureTests
{
    readonly ITestOutputHelper _output;

    public RutorParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Rutor.host;
    }

    public static IEnumerable<object[]> FixtureCases()
    {
        foreach (var kv in RutorCategories.Map.OrderBy(x => int.Parse(x.Key)))
            yield return new object[] { kv.Key, $"browse_{kv.Key}.html", kv.Value.Types };
    }

    [Fact]
    public void FixtureCases_CoverEntireCategoryMap()
    {
        Assert.Equal(RutorCategories.Map.Count, FixtureCases().Count());
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void ParseTorrentsFromPage_Fixture_YieldsTypedTorrents(string cat, string fixtureFile, string[] expectedTypes)
    {
        string html = FixtureLoader.Read($"Rutor/{fixtureFile}");
        List<TorrentBaseDetails> torrents = RutorParser.ParseTorrentsFromPage(html, cat);

        _output.WriteLine($"cat={cat} fixture={fixtureFile} parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  name={t.name} | orig={t.originalname} | year={t.relased} | {t.title}");

        // Cat 17 filters to titles containing " UKR" — yield is intentionally low.
        int minExpected = cat == "17" ? 1 : 40;
        Assert.True(torrents.Count >= minExpected, $"expected >={minExpected} torrents for cat {cat}, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("rutor", t.trackerName);
            Assert.Equal(expectedTypes, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(AppInit.conf.Rutor.host.TrimEnd('/') + "/", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.magnet));
            Assert.StartsWith("magnet:?xt=", t.magnet, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.NotEqual(default, t.createTime);
            if (cat == "17")
                Assert.Contains(" UKR", t.title, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SportFixture_TypedAsSport()
    {
        string html = FixtureLoader.Read("Rutor/browse_13.html");
        var torrents = RutorParser.ParseTorrentsFromPage(html, "13");

        Assert.True(torrents.Count >= 40);
        Assert.All(torrents, t =>
        {
            Assert.Equal(new[] { "sport" }, t.types);
            Assert.DoesNotContain("movie", t.types);
        });
    }

    [Fact]
    public void ParseTorrentsFromPage_UnknownCategory_ReturnsEmpty()
    {
        string html = FixtureLoader.Read("Rutor/browse_1.html");
        Assert.Empty(RutorParser.ParseTorrentsFromPage(html, "2"));
    }

    [Fact]
    public void DryRun_AllFixtures_ReportParseRates()
    {
        foreach (object[] row in FixtureCases())
        {
            string cat = (string)row[0];
            string file = (string)row[1];
            string[] types = (string[])row[2];
            string html = FixtureLoader.Read($"Rutor/{file}");
            var torrents = RutorParser.ParseTorrentsFromPage(html, cat);
            int withYear = torrents.Count(t => t.relased > 0);
            int withOriginal = torrents.Count(t => !string.IsNullOrWhiteSpace(t.originalname));
            _output.WriteLine(
                $"DRY-RUN cat={cat,-3} types=[{string.Join(",", types)}] " +
                $"parsed={torrents.Count,3} withYear={withYear,3} withOriginal={withOriginal,3}");
            int minExpected = cat == "17" ? 1 : 40;
            Assert.True(torrents.Count >= minExpected, $"dry-run cat {cat}: low yield {torrents.Count}");
        }
    }
}
