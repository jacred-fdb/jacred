using JacRed.Infrastructure.Trackers.Megapeer;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Megapeer;

/// <summary>
/// Regression against captured Megapeer browse.php HTML (cat 79, 2026-08).
/// Listing rows now use colspan on the title cell; date regex must accept &lt;td[^>]*>.
/// </summary>
public class MegapeerParserFixtureTests
{
    readonly ITestOutputHelper _output;

    public MegapeerParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Megapeer.host;
    }

    [Fact]
    public void ParseTorrentsFromPage_Fixture_YieldsMovies()
    {
        string html = FixtureLoader.Read("Megapeer/browse_79.html");
        var torrents = MegapeerParser.ParseTorrentsFromPage(html, "79");

        _output.WriteLine($"parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  name={t.name} | year={t.relased} | {t.title}");

        Assert.True(torrents.Count >= 40, $"expected >=40 torrents, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("megapeer", t.trackerName);
            Assert.Equal(new[] { "movie" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(AppInit.conf.Megapeer.host.TrimEnd('/') + "/", t.url, StringComparison.Ordinal);
            Assert.Contains("/torrent/", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.downloadId));
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.NotEqual(default, t.createTime);
        });
    }

    [Fact]
    public void ParseTorrentsFromPage_ColspanRow_ParsesDateAndDownload()
    {
        string html = FixtureLoader.Read("Megapeer/browse_79.html");
        var torrents = MegapeerParser.ParseTorrentsFromPage(html, "79");

        MegapeerDetails first = torrents[0];
        Assert.Equal("211657", first.downloadId);
        Assert.Equal(new DateTime(2026, 8, 25), first.createTime);
        Assert.Contains("Огниво против Волшебной Скважины", first.title, StringComparison.Ordinal);
        Assert.Equal("Огниво против Волшебной Скважины", first.name);
        Assert.Equal(2025, first.relased);
        Assert.Equal("1.57 GB", first.sizeName);
        Assert.EndsWith("/torrent/211657", first.url, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseTorrentsFromPage_LegacyBareTdRow_StillParses()
    {
        string html = FixtureLoader.Read("Megapeer/browse_79.html");
        var torrents = MegapeerParser.ParseTorrentsFromPage(html, "79");

        MegapeerDetails legacy = torrents.Single(t => t.downloadId == "211111");
        Assert.Equal(new DateTime(2026, 8, 13), legacy.createTime);
        Assert.Contains("Дед Фомич", legacy.title, StringComparison.Ordinal);
        Assert.Equal("Дед Фомич", legacy.name);
        Assert.Equal(2026, legacy.relased);
    }

    [Fact]
    public void ParseTorrentsFromPage_EmptyOrInvalid_ReturnsEmpty()
    {
        Assert.Empty(MegapeerParser.ParseTorrentsFromPage("", "79"));
        Assert.Empty(MegapeerParser.ParseTorrentsFromPage("<html></html>", "79"));
    }
}
