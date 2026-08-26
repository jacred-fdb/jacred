using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.SubsPlease;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.SubsPlease;

/// <summary>
/// SubsPlease API fixtures — 1080 only, Batch packs, xl size.
/// Refresh: python3 scripts/dry_run_subsplease_parser.py --refresh-fixtures
/// </summary>
public class SubsPleaseParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string Host = "https://subsplease.org";

    public SubsPleaseParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.SubsPlease.host;
    }

    [Fact]
    public void Constants_MatchContract()
    {
        Assert.Equal("subsplease", SubsPleaseParser.TrackerName);
        Assert.Equal("1080", SubsPleaseParser.PreferredRes);
    }

    [Fact]
    public void ParseLatestJson_Fixture_KeepsOnly1080()
    {
        string json = FixtureLoader.Read("SubsPlease/latest.json");
        var items = SubsPleaseParser.ParseLatestOrSearchJson(json, Host);

        _output.WriteLine($"latest1080={items.Count}");
        Assert.True(items.Count >= 1);
        Assert.All(items, t =>
        {
            Assert.Equal("subsplease", t.trackerName);
            Assert.Equal(new[] { "anime" }, t.types);
            Assert.Equal(1080, t.quality);
            Assert.StartsWith(Host + "/shows/", t.url, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("res=1080", t.url, StringComparison.Ordinal);
            Assert.Contains("ep=", t.url, StringComparison.Ordinal);
            Assert.StartsWith("magnet:?xt=urn:btih:", t.magnet, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.False(string.IsNullOrWhiteSpace(t.infoHash));
            Assert.Contains("(1080p)", t.title, StringComparison.Ordinal);
            Assert.DoesNotContain("(720p)", t.title, StringComparison.Ordinal);
            Assert.DoesNotContain("(480p)", t.title, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ParseShowJson_Sid11_IncludesBatches()
    {
        string json = FixtureLoader.Read("SubsPlease/show_sid11.json");
        var items = SubsPleaseParser.ParseShowJson(json, Host, "100-man-no-inochi-no-ue-ni-ore-wa-tatte-iru", "11");

        _output.WriteLine($"show items={items.Count} batches={items.Count(t => t.isBatch)}");
        Assert.True(items.Count >= 2);
        Assert.Contains(items, t => t.isBatch && t.episode == "13-24");
        Assert.Contains(items, t => t.isBatch && t.episode == "01-12");
        Assert.Contains(items, t => !t.isBatch && t.episode == "24");

        var batch = items.First(t => t.episode == "13-24");
        Assert.Contains("[Batch]", batch.title, StringComparison.Ordinal);
        Assert.Equal("11", batch.showSid);
        Assert.Equal("100-man-no-inochi-no-ue-ni-ore-wa-tatte-iru", batch.page);
        Assert.False(string.IsNullOrWhiteSpace(batch._sn)); // nyaa torrent on show API
        Assert.True(SubsPleaseParser.TryParseXl(batch.magnet) > 10_000_000_000L);
        Assert.Equal(
            $"{Host}/shows/100-man-no-inochi-no-ue-ni-ore-wa-tatte-iru/?ep=13-24&res=1080",
            batch.url);
    }

    [Fact]
    public void ParseSchedule_AndShowIndex_AndSidHtml()
    {
        var scheduleSlugs = SubsPleaseParser.ParseSchedulePageSlugs(FixtureLoader.Read("SubsPlease/schedule.json"));
        Assert.True(scheduleSlugs.Count >= 1);
        _output.WriteLine($"scheduleSlugs={scheduleSlugs.Count}");

        var showSlugs = SubsPleaseParser.ParseShowSlugsFromIndexHtml(
            FixtureLoader.Read("SubsPlease/shows_index_snippet.html"));
        Assert.True(showSlugs.Count >= 10);
        Assert.Contains("100-man-no-inochi-no-ue-ni-ore-wa-tatte-iru", showSlugs);

        string sid = SubsPleaseParser.ExtractShowSidFromHtml(
            FixtureLoader.Read("SubsPlease/show_page_sid11.html"));
        Assert.Equal("11", sid);
    }

    [Theory]
    [InlineData("13-24", true)]
    [InlineData("01-12", true)]
    [InlineData("06", false)]
    [InlineData("Movie", false)]
    public void IsBatchEpisode_DetectsRanges(string ep, bool expect)
    {
        Assert.Equal(expect, SubsPleaseParser.IsBatchEpisode(ep));
    }

    [Fact]
    public void StableUrlId_IsDeterministicPositive()
    {
        int a = SubsPleaseParser.StableUrlId("13-24");
        int b = SubsPleaseParser.StableUrlId("13-24");
        int c = SubsPleaseParser.StableUrlId("01-12");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(a > 0);
    }

    [Fact]
    public void IsLimitReached_DetectsMarker()
    {
        Assert.True(SubsPleaseParser.IsLimitReached("{\"limit_reached\":true}"));
        Assert.False(SubsPleaseParser.IsLimitReached("{\"a\":1}"));
    }

    [Fact]
    public void ParseLatest_Empty_ReturnsEmpty()
    {
        Assert.Empty(SubsPleaseParser.ParseLatestOrSearchJson("", Host));
        Assert.Empty(SubsPleaseParser.ParseLatestOrSearchJson("[]", Host));
    }
}
