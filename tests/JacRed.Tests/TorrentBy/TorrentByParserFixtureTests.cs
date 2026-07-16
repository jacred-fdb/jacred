using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.TorrentBy;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.TorrentBy;

/// <summary>
/// Regression tests against captured torrent.by HTML.
/// Refresh: python3 scripts/dry_run_torrentby_parser.py --refresh-fixtures
/// </summary>
public class TorrentByParserFixtureTests
{
    readonly ITestOutputHelper _output;

    public TorrentByParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.TorrentBy.host;
    }

    public static IEnumerable<object[]> FixtureCases()
    {
        foreach (var kv in TorrentByCategories.Map.OrderBy(x => x.Key))
            yield return new object[] { kv.Key, $"browse_{kv.Key}.html", kv.Value.Types };
    }

    [Fact]
    public void FixtureCases_CoverEntireCategoryMap()
    {
        Assert.Equal(TorrentByCategories.Map.Count, FixtureCases().Count());
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void ParseTorrentsFromHtml_Fixture_YieldsTypedTorrents(string cat, string fixtureFile, string[] expectedTypes)
    {
        string html = FixtureLoader.Read($"TorrentBy/{fixtureFile}");
        List<TorrentBaseDetails> torrents = TorrentByParser.ParseTorrentsFromHtml(html, cat);

        _output.WriteLine($"cat={cat} fixture={fixtureFile} parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  name={t.name} | orig={t.originalname} | year={t.relased} | {t.title}");

        // Category pages typically have ~70–105 rows; require a solid parse yield.
        Assert.True(torrents.Count >= 30, $"expected >=30 torrents for cat {cat}, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("torrentby", t.trackerName);
            Assert.Equal(expectedTypes, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(AppInit.conf.TorrentBy.host.TrimEnd('/') + "/", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.magnet));
            Assert.StartsWith("magnet:?xt=", t.magnet, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.NotEqual(default, t.createTime);
        });
    }

    [Fact]
    public void ParseTorrentsFromHtml_UnknownCategory_ReturnsEmpty()
    {
        string html = FixtureLoader.Read("TorrentBy/browse_films.html");
        Assert.Empty(TorrentByParser.ParseTorrentsFromHtml(html, "music"));
    }

    [Fact]
    public void DryRun_AllFixtures_ReportParseRates()
    {
        foreach (object[] row in FixtureCases())
        {
            string cat = (string)row[0];
            string file = (string)row[1];
            string[] types = (string[])row[2];
            string html = FixtureLoader.Read($"TorrentBy/{file}");
            var torrents = TorrentByParser.ParseTorrentsFromHtml(html, cat);
            int withYear = torrents.Count(t => t.relased > 0);
            int withOriginal = torrents.Count(t => !string.IsNullOrWhiteSpace(t.originalname));
            _output.WriteLine(
                $"DRY-RUN cat={cat,-10} types=[{string.Join(",", types)}] " +
                $"parsed={torrents.Count,3} withYear={withYear,3} withOriginal={withOriginal,3}");
            Assert.True(torrents.Count >= 30, $"dry-run cat {cat}: low yield {torrents.Count}");
        }
    }
}
