using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.Viruseproject;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Viruseproject;

/// <summary>
/// Regression tests against captured viruseproject.tv HTML.
/// Refresh: python3 scripts/dry_run_viruseproject_parser.py --refresh-fixtures
/// </summary>
public class ViruseprojectParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string Host = "https://viruseproject.tv";

    public ViruseprojectParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Viruseproject.host;
    }

    public static IEnumerable<object[]> BrowseFixtureCases()
    {
        foreach (var kv in ViruseprojectCategories.Map.OrderBy(x => x.Key, StringComparer.Ordinal))
            yield return new object[] { kv.Key, $"browse_{kv.Key}.html", kv.Value.Types };
    }

    [Fact]
    public void Categories_CoverExpectedSlugs()
    {
        Assert.Equal(5, ViruseprojectCategories.Map.Count);
        Assert.True(ViruseprojectParser.TryGetTypes("movies", out string[] types));
        Assert.Equal(new[] { "movie" }, types);
        Assert.True(ViruseprojectParser.TryGetTypes("documentary", out types));
        Assert.Equal(new[] { "docuserial", "documovie" }, types);
        Assert.Equal(10, ViruseprojectParser.GetPageStep("movies"));
        Assert.Equal(6, ViruseprojectParser.GetPageStep("cartoons"));
    }

    [Fact]
    public void BrowseFixtureCases_CoverEntireCategoryMap()
    {
        Assert.Equal(ViruseprojectCategories.Map.Count, BrowseFixtureCases().Count());
    }

    [Theory]
    [MemberData(nameof(BrowseFixtureCases))]
    public void ExtractPostUrls_BrowseFixture_YieldsPosts(string cat, string fixtureFile, string[] expectedTypes)
    {
        string html = FixtureLoader.Read($"Viruseproject/{fixtureFile}");
        var urls = ViruseprojectParser.ExtractPostUrls(html, Host);

        _output.WriteLine($"cat={cat} fixture={fixtureFile} posts={urls.Count} types=[{string.Join(",", expectedTypes)}]");
        foreach (var u in urls.Take(3))
            _output.WriteLine($"  {u}");

        Assert.True(urls.Count >= 1, $"expected >=1 post urls for cat {cat}, got {urls.Count}");
        Assert.All(urls, u =>
        {
            Assert.StartsWith(Host, u, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/releases/", u, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ParseDetailHtml_Fixture_YieldsOneRecordPerQuality()
    {
        string html = FixtureLoader.Read("Viruseproject/detail_sample.html");
        string postUrl = $"{Host}/releases/serials/shugar-sugar-sezon-2";
        List<ViruseprojectDetails> torrents = ViruseprojectParser.ParseDetailHtml(html, postUrl, Host, new[] { "serial" });

        _output.WriteLine($"parsed={torrents.Count}");
        foreach (var t in torrents)
            _output.WriteLine($"  {t.url} | q={t.quality} | {t.sizeName} | {t.title}");

        Assert.True(torrents.Count >= 2, $"expected >=2 quality records, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("viruseproject", t.trackerName);
            Assert.Equal(new[] { "serial" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(postUrl + "#q=", t.url, StringComparison.Ordinal);
            Assert.Contains("&id=", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.downloadUri));
            Assert.Contains("/download/", t.downloadUri, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.True(t.sid >= 1);
            Assert.True(t.quality > 0);
            Assert.Equal(DateTimeKind.Utc, t.createTime.Kind);
        });

        ViruseprojectDetails first = torrents.First();
        Assert.Equal("Шугар", first.name);
        Assert.Equal("Sugar", first.originalname);
        Assert.Equal(2026, first.relased);
        Assert.Equal("WEBRip", first.videotype);
        Assert.Contains("[WEBRip]", first.title, StringComparison.Ordinal);
        Assert.Contains("[1080p]", torrents.First(t => t.quality == 1080).title, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectLastPage_FromPaginationEnd()
    {
        string html = FixtureLoader.Read("Viruseproject/browse_movies.html");
        int last = ViruseprojectParser.DetectLastPage(html, 10);
        Assert.True(last >= 2, $"expected last page >=2, got {last}");
        Assert.Equal(1, ViruseprojectParser.DetectLastPage("", 10));
        Assert.Equal(1, ViruseprojectParser.DetectLastPage(null, 10));
        Assert.Equal(3, ViruseprojectParser.DetectLastPage(
            @"<li class=""pagination-end""><a href=""/releases/movies?start=20"">end</a></li>", 10));
    }

    [Fact]
    public void ExtractPostUrls_Empty_ReturnsEmpty()
    {
        Assert.Empty(ViruseprojectParser.ExtractPostUrls("", Host));
        Assert.Empty(ViruseprojectParser.ExtractPostUrls("<html></html>", Host));
    }

    [Fact]
    public void ParseDetailHtml_Empty_ReturnsEmpty()
    {
        Assert.Empty(ViruseprojectParser.ParseDetailHtml("", $"{Host}/x", Host, new[] { "movie" }));
        Assert.Empty(ViruseprojectParser.ParseDetailHtml("<html></html>", $"{Host}/x", Host, new[] { "movie" }));
    }

    [Theory]
    [InlineData("Ведьмак: Сирены глубин / The Witcher: Sirens of the Deep / 2025", "Ведьмак: Сирены глубин", "The Witcher: Sirens of the Deep")]
    [InlineData("Вершина / Apex / 2026", "Вершина", "Apex")]
    [InlineData("Соперники / Rivals / сезон 2 / 1-3 из 12", "Соперники", "Rivals")]
    [InlineData("Адская Кухня 11 (Hell's Kitchen 11)", "Адская Кухня 11", "Hell's Kitchen 11")]
    [InlineData("Шествие смерти (Death Parade)", "Шествие смерти", "Death Parade")]
    [InlineData("Фоллаут / Fallout / сезон 2", "Фоллаут", "Fallout")]
    public void ParseNames_MatchesGoCases(string raw, string wantRu, string wantEn)
    {
        var (ru, en) = ViruseprojectParser.ParseNames(raw);
        Assert.Equal(wantRu, ru);
        Assert.Equal(wantEn, en);
    }

    [Theory]
    [InlineData("Четверг, 13 Февраль 2025 00:00", 2025, 2, 13)]
    [InlineData("Вторник, 12 Май 2026 00:00", 2026, 5, 12)]
    [InlineData("Понедельник, 23 Мая 2026 00:00", 2026, 5, 23)]
    [InlineData("Среда, 07 Май 2014 00:00", 2014, 5, 7)]
    public void ParseRussianDate_MatchesGoCases(string raw, int year, int month, int day)
    {
        DateTime got = ViruseprojectParser.ParseRussianDate(raw);
        Assert.Equal(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc), got);
    }
}
