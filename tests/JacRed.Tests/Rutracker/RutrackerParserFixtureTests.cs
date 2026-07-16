using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.Rutracker;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Rutracker;

/// <summary>
/// Regression tests against captured Rutracker forum HTML.
/// Refresh: python3 scripts/dry_run_rutracker_parser.py --refresh-fixtures
/// </summary>
public class RutrackerParserFixtureTests
{
    readonly ITestOutputHelper _output;

    // Representative sample (not full 211-forum map).
    static readonly (string id, string file)[] SampleFixtures =
    {
        ("1950", "forum_1950.html"),
        ("842", "forum_842.html"),
        ("1105", "forum_1105.html"),
        ("1392", "forum_1392.html"),
        ("709", "forum_709.html"),
        ("24", "forum_24.html"),
    };

    public RutrackerParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Rutracker.host;
    }

    public static IEnumerable<object[]> FixtureCases()
    {
        foreach (var (id, file) in SampleFixtures)
            yield return new object[] { id, file, RutrackerCategories.Map[id].Types };
    }

    [Fact]
    public void FixtureCases_CoverRepresentativeSample()
    {
        Assert.Equal(6, FixtureCases().Count());
        Assert.Contains(SampleFixtures, x => x.id == "1392"); // former sport orphan
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void ParseTorrentsFromPage_Fixture_YieldsTypedTorrents(string cat, string fixtureFile, string[] expectedTypes)
    {
        string html = FixtureLoader.Read($"Rutracker/{fixtureFile}");
        List<TorrentDetails> torrents = RutrackerParser.ParseTorrentsFromPage(html, cat);

        _output.WriteLine($"cat={cat} fixture={fixtureFile} parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  name={t.name} | orig={t.originalname} | year={t.relased} | {t.title}");

        // Forum pages vary; require a usable yield from the listing shape.
        Assert.True(torrents.Count >= 8, $"expected >=8 torrents for cat {cat}, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("rutracker", t.trackerName);
            Assert.Equal(expectedTypes, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(AppInit.conf.Rutracker.host.TrimEnd('/') + "/", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.NotEqual(default, t.createTime);
        });
    }

    [Fact]
    public void SportOrphanFixture_IsTypedSport_NotDropped()
    {
        string html = FixtureLoader.Read("Rutracker/forum_1392.html");
        var torrents = RutrackerParser.ParseTorrentsFromPage(html, "1392");

        Assert.True(torrents.Count >= 20, $"expected sport yield, got {torrents.Count}");
        Assert.All(torrents, t =>
        {
            Assert.Equal(new[] { "sport" }, t.types);
            Assert.DoesNotContain("movie", t.types);
        });
    }

    [Fact]
    public void ParseTorrentsFromPage_UnknownCategory_ReturnsEmpty()
    {
        string html = FixtureLoader.Read("Rutracker/forum_1950.html");
        Assert.Empty(RutrackerParser.ParseTorrentsFromPage(html, "999999"));
    }

    [Fact]
    public void DryRun_SampleFixtures_ReportParseRates()
    {
        foreach (object[] row in FixtureCases())
        {
            string cat = (string)row[0];
            string file = (string)row[1];
            string[] types = (string[])row[2];
            string html = FixtureLoader.Read($"Rutracker/{file}");
            var torrents = RutrackerParser.ParseTorrentsFromPage(html, cat);
            int withYear = torrents.Count(t => t.relased > 0);
            int withOriginal = torrents.Count(t => !string.IsNullOrWhiteSpace(t.originalname));
            _output.WriteLine(
                $"DRY-RUN cat={cat,-6} types=[{string.Join(",", types)}] " +
                $"parsed={torrents.Count,3} withYear={withYear,3} withOriginal={withOriginal,3}");
            Assert.True(torrents.Count >= 8, $"dry-run cat {cat}: low yield {torrents.Count}");
        }
    }
}
