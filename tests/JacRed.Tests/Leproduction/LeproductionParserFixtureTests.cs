using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.Leproduction;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Leproduction;

/// <summary>
/// Regression tests against captured le-production.tv HTML.
/// Refresh: python3 scripts/dry_run_leproduction_parser.py --refresh-fixtures
/// </summary>
public class LeproductionParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string Host = "https://www.le-production.tv";

    public LeproductionParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Leproduction.host;
    }

    public static IEnumerable<object[]> BrowseFixtureCases()
    {
        foreach (var kv in LeproductionCategories.Map.OrderBy(x => x.Key, StringComparer.Ordinal))
            yield return new object[] { kv.Key, $"browse_{kv.Key}.html", kv.Value.Types };
    }

    [Fact]
    public void Categories_CoverExpectedSlugs()
    {
        Assert.Equal(6, LeproductionCategories.Map.Count);
        Assert.True(LeproductionParser.TryGetTypes("film", out string[] types));
        Assert.Equal(new[] { "movie" }, types);
    }

    [Fact]
    public void BrowseFixtureCases_CoverEntireCategoryMap()
    {
        Assert.Equal(LeproductionCategories.Map.Count, BrowseFixtureCases().Count());
    }

    [Theory]
    [MemberData(nameof(BrowseFixtureCases))]
    public void ExtractPostUrls_BrowseFixture_YieldsPosts(string cat, string fixtureFile, string[] expectedTypes)
    {
        string html = FixtureLoader.Read($"Leproduction/{fixtureFile}");
        var urls = LeproductionParser.ExtractPostUrls(html, Host);

        _output.WriteLine($"cat={cat} fixture={fixtureFile} posts={urls.Count} types=[{string.Join(",", expectedTypes)}]");
        foreach (var u in urls.Take(3))
            _output.WriteLine($"  {u}");

        Assert.True(urls.Count >= 1, $"expected >=1 post urls for cat {cat}, got {urls.Count}");
        Assert.All(urls, u =>
        {
            Assert.StartsWith(Host, u, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".html", u, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ParseDetailHtml_Fixture_YieldsTypedTorrents()
    {
        string html = FixtureLoader.Read("Leproduction/detail_sample.html");
        string postUrl = $"{Host}/film/sample-movie-2024.html";
        List<TorrentDetails> torrents = LeproductionParser.ParseDetailHtml(html, postUrl, new[] { "movie" });

        _output.WriteLine($"parsed={torrents.Count}");
        foreach (var t in torrents)
            _output.WriteLine($"  {t.url} | sid={t.sid} | {t.sizeName} | {t.title}");

        Assert.True(torrents.Count >= 1, $"expected >=1 torrents, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("leproduction", t.trackerName);
            Assert.Equal(new[] { "movie" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(postUrl + "?", t.url, StringComparison.Ordinal);
            Assert.Contains("&id=", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.magnet));
            Assert.StartsWith("magnet:", t.magnet, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.True(t.sid >= 0);
            Assert.True(t.pir >= 0);
        });

        TorrentDetails first = torrents.First();
        Assert.Contains("Тестовый", first.name, StringComparison.Ordinal);
        Assert.Equal(2024, first.relased);
        Assert.NotNull(LeproductionParser.ExtractTorrentId(first.url));
    }

    [Fact]
    public void DetectLastPage_FindsMaxPage()
    {
        string html = @"<a href=""/film/page/2/"">2</a><a href=""/film/page/12/"">12</a><a href=""/film/page/5/"">5</a>";
        Assert.Equal(12, LeproductionParser.DetectLastPage(html));
        Assert.Equal(1, LeproductionParser.DetectLastPage(""));
        Assert.Equal(1, LeproductionParser.DetectLastPage(null));
    }

    [Fact]
    public void ExtractPostUrls_Empty_ReturnsEmpty()
    {
        Assert.Empty(LeproductionParser.ExtractPostUrls("", Host));
        Assert.Empty(LeproductionParser.ExtractPostUrls("<html></html>", Host));
    }

    [Fact]
    public void ParseDetailHtml_Empty_ReturnsEmpty()
    {
        Assert.Empty(LeproductionParser.ParseDetailHtml("", $"{Host}/x.html", new[] { "movie" }));
        Assert.Empty(LeproductionParser.ParseDetailHtml("<html></html>", $"{Host}/x.html", new[] { "movie" }));
    }

    [Fact]
    public void ExtractMagnet_FromHref()
    {
        string html = @"<a href=""magnet:?xt=urn:btih:ABC&amp;dn=x"">m</a>";
        string magnet = LeproductionParser.ExtractMagnet(html);
        Assert.StartsWith("magnet:?xt=urn:btih:ABC", magnet, StringComparison.OrdinalIgnoreCase);
    }
}
