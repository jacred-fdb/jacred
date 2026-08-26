using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.Anifilm;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Anifilm;

/// <summary>
/// Regression against captured Anifilm HTML (Go-compatible regexes).
/// Refresh: python3 scripts/dry_run_anifilm_parser.py --refresh-fixtures
/// </summary>
public class AnifilmParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string Host = "https://anifilm.pro";

    public AnifilmParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Anifilm.host;
    }

    [Fact]
    public void Categories_CoverExpectedSlugs()
    {
        Assert.Equal(8, AnifilmCategories.Map.Count);
        Assert.True(AnifilmCategories.Map.TryGetValue("serials", out var serials));
        Assert.Equal(new[] { "anime" }, serials.Types);
        Assert.True(AnifilmCategories.Map.TryGetValue("dorams", out var dorams));
        Assert.Equal(new[] { "serial" }, dorams.Types);
        Assert.Equal(70, AnifilmCategories.MaxPages(serials, fullparse: true));
        Assert.Equal(2, AnifilmCategories.MaxPages(serials, fullparse: false));
    }

    [Fact]
    public void ParseListingHtml_ListingFixture_YieldsTypedItems()
    {
        string html = FixtureLoader.Read("Anifilm/listing_serials.html");
        var items = AnifilmParser.ParseListingHtml(html, Host, new[] { "anime" }, DateTime.UtcNow);

        _output.WriteLine($"items={items.Count}");
        foreach (var t in items.Take(3))
            _output.WriteLine($"  {t.url} | {t.title} | year={t.relased}");

        Assert.True(items.Count >= 2, $"expected >=2 items, got {items.Count}");
        Assert.All(items, t =>
        {
            Assert.Equal("anifilm", t.trackerName);
            Assert.Equal(new[] { "anime" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.StartsWith(Host + "/releases/", t.url, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, t.sid);
        });

        // Synthetic fixture (live anifilm.pro often needs CF/cookie).
        if (items.Count >= 2 && items[0].url.Contains("/releases/test-show-1", StringComparison.Ordinal))
        {
            Assert.Equal("Тестовое аниме", items[0].name);
            Assert.Equal("Test Anime", items[0].originalname);
            Assert.Contains("12", items[0].title, StringComparison.Ordinal);
            Assert.Equal(2024, items[0].relased);
            Assert.Equal(2023, items[1].relased);
        }
    }

    [Fact]
    public void ExtractTorrentDownloadPath_DetailFixture_Prefers1080p()
    {
        string html = FixtureLoader.Read("Anifilm/detail_sample.html");
        var (tid, is1080p) = AnifilmParser.ExtractTorrentDownloadPath(html);

        _output.WriteLine($"tid={tid} is1080p={is1080p}");
        Assert.Equal("releases/download-torrent/101", tid);
        Assert.True(is1080p);
    }

    [Fact]
    public void ExtractTorrentDownloadPath_FallbackWithout1080()
    {
        string html = """
            <li class="release__torrents-item">
              720p
              <a href="/releases/download-torrent/55">скачать</a>
            </li>
            """;
        var (tid, is1080p) = AnifilmParser.ExtractTorrentDownloadPath(html);
        Assert.Equal("releases/download-torrent/55", tid);
        Assert.False(is1080p);
    }

    [Fact]
    public void ParseListingHtml_EmptyOrInvalid_ReturnsEmpty()
    {
        Assert.Empty(AnifilmParser.ParseListingHtml("", Host, new[] { "anime" }, DateTime.UtcNow));
        Assert.Empty(AnifilmParser.ParseListingHtml("<html></html>", Host, new[] { "anime" }, DateTime.UtcNow));
        Assert.Null(AnifilmParser.ExtractTorrentDownloadPath("").tid);
        Assert.Null(AnifilmParser.ExtractTorrentDownloadPath("<html></html>").tid);
    }
}
