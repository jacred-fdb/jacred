using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.Rudub;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Rudub;

/// <summary>
/// RuDub browse cards — keep HD 1080 / HD 2160 only.
/// Refresh: python3 scripts/dry_run_rudub_parser.py --user U --password P --refresh-fixtures
/// </summary>
public class RudubParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string Host = "https://r4.rudub.world";

    public RudubParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Rudub.host;
    }

    [Fact]
    public void Constants_MatchSiteContract()
    {
        Assert.Equal("rudub", RudubParser.TrackerName);
        Assert.Equal("card__torlist__browse_2", RudubParser.ValidationMarker);
        Assert.Equal("/download2.php", RudubParser.EndpointDownload);
        Assert.Equal(new[] { 4, 5 }, RudubParser.PreferredVideoFormats);
    }

    [Theory]
    [InlineData("Пробуждение (Ontwaak) Сезон 1 (HD1080p WEBRip)", true)]
    [InlineData("Show (Original) (HD2160p WEBRip)", true)]
    [InlineData("Show (Original) (BD1080p)", true)]
    [InlineData("Пробуждение (Ontwaak) Сезон 1 (WEBRip XviD)", false)]
    [InlineData("Пробуждение (Ontwaak) Сезон 1 (HD720p WEBRip)", false)]
    [InlineData("Show (Original) (WEBRip x264)", false)]
    [InlineData("Show (Original) (720p)", false)]
    public void IsPreferredQualityTitle_GatesLadder(string title, bool expected)
    {
        Assert.Equal(expected, RudubParser.IsPreferredQualityTitle(title));
    }

    [Fact]
    public void ParseTorrentListFromHtml_Fixture_Keeps1080DropsSdAnd720()
    {
        string html = FixtureLoader.Read("Rudub/listing_sample.html");
        var items = RudubParser.ParseTorrentListFromHtml(html, Host);

        _output.WriteLine($"kept={items.Count}");
        Assert.True(items.Count >= 4, $"expected >=4 HD1080 cards, got {items.Count}");

        Assert.All(items, t =>
        {
            Assert.Equal("rudub", t.trackerName);
            Assert.Equal(new[] { "serial" }, t.types);
            Assert.StartsWith(Host + "/details.php?id=", t.url, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Host + "/download2.php?id=", t.downloadUri, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.originalname));
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.True(t.quality is 1080 or 2160, $"quality={t.quality} title={t.title}");
            Assert.DoesNotContain("XviD", t.title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HD720p", t.title, StringComparison.OrdinalIgnoreCase);
            Assert.True(RudubParser.IsPreferredQualityTitle(t.title));
        });

        Assert.Contains(items, t => t.url.Contains("id=54677", StringComparison.Ordinal));
        Assert.DoesNotContain(items, t => t.url.Contains("id=54680", StringComparison.Ordinal)); // XviD
        Assert.DoesNotContain(items, t => t.url.Contains("id=54678", StringComparison.Ordinal)); // HD720

        var first = items.First(t => t.url.Contains("id=54677", StringComparison.Ordinal));
        Assert.Equal("Пробуждение", first.name);
        Assert.Equal("Ontwaak", first.originalname);
        Assert.Equal(1080, first.quality);
        Assert.Equal(1, first.sid);
        Assert.Equal(0, first.pir);
        Assert.Equal("18.68 GB", first.sizeName);
        Assert.Equal(new DateTime(2026, 8, 11, 22, 58, 15, DateTimeKind.Utc), first.createTime);
        Assert.Equal($"{Host}/download2.php?id=54677", first.downloadUri);
        _output.WriteLine($"first: {first.name} / {first.originalname} q={first.quality}");
    }

    [Fact]
    public void ParseTorrentListFromHtml_Empty_ReturnsEmpty()
    {
        Assert.Empty(RudubParser.ParseTorrentListFromHtml("", Host));
        Assert.Empty(RudubParser.ParseTorrentListFromHtml("<html></html>", Host));
        Assert.Empty(RudubParser.ParseTorrentListFromHtml("<div class=\"card__torlist__browse_2\"></div>", ""));
    }

    [Fact]
    public void TypesEqual_Works()
    {
        Assert.True(RudubParser.TypesEqual(null, null));
        Assert.False(RudubParser.TypesEqual(new[] { "serial" }, null));
        Assert.True(RudubParser.TypesEqual(new[] { "serial" }, new[] { "serial" }));
        Assert.False(RudubParser.TypesEqual(new[] { "serial" }, new[] { "movie" }));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(0, 0, 10, 0, 9)]
    [InlineData(0, 0, 50, 0, 49)]
    [InlineData(0, 0, 200, 0, 99)] // clamped to MaxLimitPages
    [InlineData(5, 12, 0, 5, 12)]
    [InlineData(5, 12, 50, 5, 12)] // explicit range wins over limit_page
    [InlineData(12, 5, 0, 5, 12)] // swapped
    public void ResolvePageRange_MatchesContract(int from, int to, int limit, int wantStart, int wantEnd)
    {
        RudubSyncService.ResolvePageRange(from, to, limit, out int start, out int end);
        Assert.Equal(wantStart, start);
        Assert.Equal(wantEnd, end);
    }

    [Fact]
    public void MaxLimitPages_Is100()
    {
        Assert.Equal(100, RudubSyncService.MaxLimitPages);
    }
}
