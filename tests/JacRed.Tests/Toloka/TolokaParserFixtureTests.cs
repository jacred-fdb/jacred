using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.Toloka;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Toloka;

/// <summary>
/// Regression tests against captured Toloka forum HTML (f96).
/// Refresh: python3 scripts/dry_run_toloka_parser.py --refresh-fixtures
/// </summary>
public class TolokaParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string MoanaTopic = "t700099";

    public TolokaParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Toloka.host;
    }

    static string Host => AppInit.conf.Toloka.host.TrimEnd('/');

    [Fact]
    public void ParseTorrentsFromPage_Fixture_YieldsTypedTorrentsWithSizeAndPeers()
    {
        string html = FixtureLoader.Read("Toloka/browse_f96.html");
        var torrents = TolokaParser.ParseTorrentsFromPage(html, "96");

        _output.WriteLine($"parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  {t.url} | sid={t.sid} pir={t.pir} | {t.sizeName} | {t.title}");

        Assert.True(torrents.Count >= 40, $"expected >=40 torrents, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("toloka", t.trackerName);
            Assert.Equal(new[] { "movie" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(Host + "/", t.url, StringComparison.Ordinal);
            Assert.Contains("/t", t.url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(t.downloadId));
            Assert.True(t.downloadId.All(char.IsDigit));
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.DoesNotContain('\u00A0', t.sizeName);
            Assert.DoesNotContain("&nbsp;", t.sizeName, StringComparison.OrdinalIgnoreCase);
            Assert.Matches(@"(?i)[0-9][0-9\.,]* (MB|GB|TB|МБ|ГБ|ТБ)", t.sizeName);
            Assert.NotEqual("0 B", t.sizeName);
            Assert.DoesNotContain("Завантажити", t.sizeName, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(default, t.createTime);
            Assert.True(t.sid >= 0);
            Assert.True(t.pir >= 0);
        });

        TolokaDetails moana = Assert.Single(torrents, t => t.url.EndsWith("/" + MoanaTopic, StringComparison.Ordinal));
        Assert.Equal("715016", moana.downloadId);
        Assert.Equal("21.39 GB", moana.sizeName);
        Assert.Equal(29, moana.sid);
        Assert.Equal(12, moana.pir);
        Assert.Contains("Moana", moana.title, StringComparison.Ordinal);
        Assert.Equal(Host + "/" + MoanaTopic, moana.url);
    }

    [Fact]
    public void ParseTorrentsFromPage_RelativeHrefs_YieldsTopicAndDownloadId()
    {
        string html =
            "<html lang=\"uk\"><table><tr></tr>\n" +
            "<tr>\n" +
            "  <td><a href=\"t700099\" class=\"topictitle\">Ваяна / Moana (2026) WEB-DL</a></td>\n" +
            "  <td><span class=\"seedmed\"><b>29</b></span><span class=\"leechmed\"><b>12</b></span></td>\n" +
            "  <td><a href=\"download.php?id=715016\">21.39&nbsp;GB</a></td>\n" +
            "  <td><span class=\"postdetails\">2026-09-08 16:58</span></td>\n" +
            "</tr>\n" +
            "</table></html>";

        var torrents = TolokaParser.ParseTorrentsFromPage(html, "96");
        TolokaDetails t = Assert.Single(torrents);
        Assert.Equal(Host + "/t700099", t.url);
        Assert.Equal("715016", t.downloadId);
        Assert.Equal("21.39 GB", t.sizeName);
        Assert.DoesNotContain('\u00A0', t.sizeName);
        Assert.Equal(29, t.sid);
        Assert.Equal(12, t.pir);
        Assert.Equal("Ваяна", t.name);
        Assert.Equal("Moana", t.originalname);
        Assert.Equal(2026, t.relased);
    }

    [Fact]
    public void ParseTorrentsFromPage_UnknownCategory_ReturnsEmpty()
    {
        string html = FixtureLoader.Read("Toloka/browse_f96.html");
        Assert.Empty(TolokaParser.ParseTorrentsFromPage(html, "999"));
    }

    [Fact]
    public void ParseTorrentsFromPage_EmptyHtml_ReturnsEmpty()
    {
        Assert.Empty(TolokaParser.ParseTorrentsFromPage("", "96"));
        Assert.Empty(TolokaParser.ParseTorrentsFromPage("<html lang=\"uk\"></html>", "96"));
    }
}
