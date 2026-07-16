using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Lostfilm;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Lostfilm;

/// <summary>
/// Regression against captured /new/ HTML.
/// Refresh: python3 scripts/dry_run_lostfilm_parser.py --refresh-fixtures
/// </summary>
public class LostfilmParserFixtureTests
{
    readonly ITestOutputHelper _output;

    public LostfilmParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Lostfilm.host;
    }

    [Fact]
    public async Task NewPageFixture_CollectsEpisodes_WithRuNamesFromHorBreaker()
    {
        string html = FixtureLoader.Read("Lostfilm/new_page1.html");
        Assert.Contains("LostFilm.TV", html);

        string host = "https://www.lostfilm.tv";
        var map = LostfilmParser.BuildHorBreakerNameMap(html);
        Assert.NotEmpty(map);

        var list = new List<TorrentDetails>();
        await LostfilmParser.CollectFromEpisodeLinks(html, host, cookie: null, list, page: 1, map);
        LostfilmParser.DedupeListByUrl(list);

        _output.WriteLine($"episodes={list.Count} mapKeys={map.Count}");
        Assert.True(list.Count >= 5, $"expected episodes, got {list.Count}");

        Assert.All(list, t =>
        {
            Assert.Equal("lostfilm", t.trackerName);
            Assert.Equal(new[] { "serial" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.Contains("/season_", t.url);
            Assert.Contains("/episode_", t.url);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.originalname));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.NotEqual(default, t.createTime);
        });

        // At least some rows should get RU names from hor-breaker map (not X:X).
        int withRu = list.Count(LostfilmParser.HasRuName);
        _output.WriteLine($"withRuName={withRu}");
        Assert.True(withRu >= 1, "expected at least one RU/EN name pair from hor-breaker map");
    }

    [Fact]
    public async Task SyntheticPage_MapsRuNameOntoEpisodeLink()
    {
        string html = FixtureLoader.Read("Lostfilm/new_page_synthetic.html");
        string host = "https://www.lostfilm.tv";
        var map = LostfilmParser.BuildHorBreakerNameMap(html);
        Assert.True(map.ContainsKey("series/Test_Show") || map.Keys.Any(k => k.Contains("Test_Show", System.StringComparison.OrdinalIgnoreCase)));

        var list = new List<TorrentDetails>();
        await LostfilmParser.CollectFromEpisodeLinks(html, host, null, list, 1, map);
        var t = Assert.Single(list);
        Assert.Equal("Тестовый сериал", t.name);
        Assert.Equal("Test Show", t.originalname);
        Assert.True(LostfilmParser.HasRuName(t));
        Assert.DoesNotContain('#', t.url);
    }

    [Fact]
    public void VerifyParseNewPageDates_UsesFixture()
    {
        string html = FixtureLoader.Read("Lostfilm/new_page1.html");
        var items = LostfilmParser.ParseNewPageDates(html, "https://www.lostfilm.tv");
        Assert.True(items.Count >= 5);
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.dateStr)));
    }
}
