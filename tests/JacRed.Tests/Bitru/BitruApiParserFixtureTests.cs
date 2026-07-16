using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.Bitru;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Bitru;

/// <summary>
/// Regression tests against captured BitRu API JSON.
/// Refresh: python3 scripts/dry_run_bitru_api.py --refresh-fixtures
/// </summary>
public class BitruApiParserFixtureTests
{
    const string Host = "https://bitru.org";

    readonly ITestOutputHelper _output;

    public BitruApiParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Bitru.host;
    }

    public static IEnumerable<object[]> FixtureCases()
    {
        yield return new object[] { "api_movie_serial_page1.json" };
        yield return new object[] { "api_video_page1.json" };
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void ParseTorrentsFromJson_Fixture_YieldsTypedTorrents(string fixtureFile)
    {
        string json = FixtureLoader.Read($"Bitru/{fixtureFile}");
        List<TorrentDetails> torrents = BitruApiParser.ParseTorrentsFromJson(json, Host);

        _output.WriteLine($"fixture={fixtureFile} parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  types=[{string.Join(",", t.types)}] name={t.name} year={t.relased} | {t.title}");

        Assert.True(torrents.Count >= 5, $"expected >=5 torrents for {fixtureFile}, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("bitru", t.trackerName);
            Assert.NotNull(t.types);
            Assert.NotEmpty(t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(Host + "/", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.NotEqual(default, t.createTime);
            Assert.False(string.IsNullOrWhiteSpace(t._sn));
            Assert.Contains("api.php?download=", t._sn, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MovieSerialFixture_ContainsMovieAndSerial()
    {
        string json = FixtureLoader.Read("Bitru/api_movie_serial_page1.json");
        var torrents = BitruApiParser.ParseTorrentsFromJson(json, Host);

        Assert.Contains(torrents, t => t.types.SequenceEqual(new[] { "movie" }));
        Assert.Contains(torrents, t => t.types.SequenceEqual(new[] { "serial" }));
    }

    [Fact]
    public void VideoFixture_MapsKnownSubsections_AndDropsOthers()
    {
        string json = FixtureLoader.Read("Bitru/api_video_page1.json");
        var torrents = BitruApiParser.ParseTorrentsFromJson(json, Host);

        Assert.True(torrents.Count >= 1);
        Assert.All(torrents, t =>
        {
            Assert.Contains(t.types[0], new[] { "documovie", "sport", "tvshow" });
        });
    }

    [Fact]
    public void Page2Fixture_ParsesWhenPresent()
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Bitru", "api_movie_serial_page2.json");
        if (!System.IO.File.Exists(path))
            return; // optional cursor fixture

        string json = FixtureLoader.Read("Bitru/api_movie_serial_page2.json");
        var torrents = BitruApiParser.ParseTorrentsFromJson(json, Host);
        Assert.True(torrents.Count >= 1);
    }
}
