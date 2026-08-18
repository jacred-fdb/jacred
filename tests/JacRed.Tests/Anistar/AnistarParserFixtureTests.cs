using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.Anistar;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Anistar;

/// <summary>
/// Regression against captured Anistar HTML (CP1251 pages, Go-compatible regexes).
/// Refresh: python3 scripts/dry_run_anistar_parser.py --refresh-fixtures
/// </summary>
public class AnistarParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string CanonHost = "https://anistar.org";

    public AnistarParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Anistar.host;
    }

    [Fact]
    public void ExtractPostUrls_ListingFixture_YieldsAbsolutePostLinks()
    {
        string html = FixtureLoader.Read("Anistar/listing_anime.html");
        var urls = AnistarParser.ExtractPostUrls(html, CanonHost);

        _output.WriteLine($"posts={urls.Count}");
        foreach (var u in urls.Take(3))
            _output.WriteLine($"  {u}");

        Assert.True(urls.Count >= 1, $"expected >=1 post urls, got {urls.Count}");
        Assert.All(urls, u =>
        {
            Assert.StartsWith(CanonHost, u, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".html", u, StringComparison.OrdinalIgnoreCase);
            Assert.Matches(@"/\d{2,}-", u);
        });
    }

    [Fact]
    public void ExtractPostUrls_AbsoluteMirrorLinks_NormalizeToCanonHost()
    {
        const string html = """
            <a href="https://v30.astar.bz/11012-gundam.html">Gundam</a>
            <a href="/11011-nano.html">Nano</a>
            """;
        var urls = AnistarParser.ExtractPostUrls(html, CanonHost);

        Assert.Equal(2, urls.Count);
        Assert.Contains("https://anistar.org/11012-gundam.html", urls);
        Assert.Contains("https://anistar.org/11011-nano.html", urls);
    }

    [Fact]
    public void DetectLastPage_ListingFixture_AtLeastOne()
    {
        string html = FixtureLoader.Read("Anistar/listing_anime.html");
        int last = AnistarParser.DetectLastPage(html);
        Assert.True(last >= 1);
        _output.WriteLine($"lastPage={last}");
    }

    [Fact]
    public void ParseDetailTorrents_DetailFixture_YieldsTypedTorrents()
    {
        string html = FixtureLoader.Read("Anistar/detail_sample.html");
        string postUrl = "https://anistar.org/12-test-show.html";
        var torrents = AnistarParser.ParseDetailTorrents(html, postUrl, new[] { "anime" });

        _output.WriteLine($"torrents={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  id={t.downloadId} | {t.title} | sid={t.sid} pir={t.pir} year={t.relased}");

        Assert.True(torrents.Count >= 1, $"expected >=1 torrents, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("anistar", t.trackerName);
            Assert.Equal(new[] { "anime" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(postUrl + "?", t.url, StringComparison.Ordinal);
            Assert.Contains("&id=", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.downloadId));
            Assert.True(t.downloadId.All(char.IsDigit));
            Assert.NotEqual(default, t.createTime);
            Assert.True(t.relased >= 1900);
        });

        // Synthetic two-block fixture (or captured live HTML).
        if (torrents.Count == 2 && torrents[0].downloadId == "1001")
        {
            Assert.Equal("Тестовое аниме", torrents[0].name);
            Assert.Equal("Test Anime", torrents[0].originalname);
            Assert.Contains("Серия 3", torrents[0].title, StringComparison.Ordinal);
            Assert.Equal(2024, torrents[0].relased);
            Assert.Equal(10, torrents[0].sid);
            Assert.Contains("Серии 1-12", torrents[1].title, StringComparison.Ordinal);
            Assert.Equal(2023, torrents[1].relased);
        }
    }

    [Fact]
    public void ParseTitleNames_SplitsRussianAndOriginal()
    {
        var (name, original) = AnistarParser.ParseTitleNames("Тестовое аниме / Test Anime");
        Assert.Equal("Тестовое аниме", name);
        Assert.Equal("Test Anime", original);
    }

    [Fact]
    public void ParseEpisodeLabel_FilmSize_IsNotEpisodeNumber()
    {
        var (filmLabel, filmNum) = AnistarParser.ParseEpisodeLabel("Фильм (3.05 Gb)");
        Assert.Equal("Фильм", filmLabel);
        Assert.Equal("film", filmNum);

        var (epLabel, epNum) = AnistarParser.ParseEpisodeLabel("Серия 7 (466.54 Mb)");
        Assert.Equal("Серия 7", epLabel);
        Assert.Equal("7", epNum);

        var (rangeLabel, rangeNum) = AnistarParser.ParseEpisodeLabel("Серии 1-12");
        Assert.Equal("Серии 1-12", rangeLabel);
        Assert.Equal("1", rangeNum);
    }

    [Fact]
    public void ParseDetailTorrents_FilmInfoD1_DoesNotUseSizeAsEpisode()
    {
        const string html = """
            <html><body>
            <h1>Гандам / Gundam</h1>
            <div id="torrent_46365_info" class="torrent">
              <div class="info_d1">Фильм (3.05 Gb)</div>
              <div>18-08-2026</div>
              <div class="li_distribute">0</div>
              <div class="li_swing">23</div>
            </div>
            </body></html>
            """;
        string postUrl = "https://anistar.org/11012-gundam.html";
        var torrents = AnistarParser.ParseDetailTorrents(html, postUrl, new[] { "anime" });

        Assert.Single(torrents);
        Assert.Equal("46365", torrents[0].downloadId);
        Assert.Contains("Фильм", torrents[0].title, StringComparison.Ordinal);
        Assert.DoesNotContain("Серия 3", torrents[0].title, StringComparison.Ordinal);
        Assert.EndsWith("?e=film&id=46365", torrents[0].url, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseDetailTorrents_EmptyHtml_ReturnsEmpty()
    {
        Assert.Empty(AnistarParser.ParseDetailTorrents("", "https://anistar.org/12-x.html", new[] { "anime" }));
        Assert.Empty(AnistarParser.ParseDetailTorrents("<html></html>", "https://anistar.org/12-x.html", new[] { "anime" }));
    }
}
