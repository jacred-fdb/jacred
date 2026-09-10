using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.Kinozal;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Kinozal;

/// <summary>
/// Regression tests against captured browse.php HTML for every mapped category.
/// Refresh fixtures:
///   python3 scripts/dry_run_kinozal_parser.py --user U --password P --refresh-fixtures
/// </summary>
public class KinozalParserFixtureTests
{
    readonly ITestOutputHelper _output;

    public KinozalParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Kinozal.host;
    }

    public static IEnumerable<object[]> FixtureCases()
    {
        foreach (var kv in KinozalCategories.Map.OrderBy(x => int.Parse(x.Key)))
            yield return new object[] { kv.Key, $"browse_c{kv.Key}.html", kv.Value.Types };
    }

    [Fact]
    public void FixtureCases_CoverEntireCategoryMap()
    {
        Assert.Equal(KinozalCategories.Map.Count, FixtureCases().Count());
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void ParseTorrentsFromPage_Fixture_YieldsTypedTorrents(string cat, string fixtureFile, string[] expectedTypes)
    {
        string html = FixtureLoader.Read($"Kinozal/{fixtureFile}");
        List<TorrentDetails> torrents = KinozalParser.ParseTorrentsFromPage(html, cat);

        _output.WriteLine($"cat={cat} fixture={fixtureFile} parsed={torrents.Count}");
        foreach (var t in torrents.Take(3))
            _output.WriteLine($"  name={t.name} | orig={t.originalname} | year={t.relased} | {t.title}");

        // Browse pages are typically 50 rows; allow some title-shape misses but require strong yield.
        Assert.True(torrents.Count >= 40, $"expected >=40 torrents for cat {cat}, got {torrents.Count}");

        Assert.All(torrents, t =>
        {
            Assert.Equal("kinozal", t.trackerName);
            Assert.Equal(expectedTypes, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith(AppInit.conf.Kinozal.host.TrimEnd('/') + "/", t.url, StringComparison.Ordinal);
            Assert.Contains("/details.php?id=", t.url, StringComparison.Ordinal);
            Assert.DoesNotContain("userdetails", t.url, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.NotEqual(default, t.createTime);
            Assert.True(t.sid >= 0);
            Assert.True(t.pir >= 0);
        });
    }

    [Fact]
    public void ParseTorrentsFromPage_Fixture_C50_ParsesTerabyteSizes()
    {
        string html = FixtureLoader.Read("Kinozal/browse_c50.html");
        List<TorrentDetails> torrents = KinozalParser.ParseTorrentsFromPage(html, "50");

        TorrentDetails tb = Assert.Single(torrents, t => t.sizeName.Contains("ТБ", StringComparison.Ordinal));
        Assert.Equal("2.278 ТБ", tb.sizeName);
        Assert.Contains("МастерШеф", tb.title, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseTorrentsFromPage_UnknownCategory_ReturnsEmpty()
    {
        string html = FixtureLoader.Read("Kinozal/browse_c8.html");
        Assert.Empty(KinozalParser.ParseTorrentsFromPage(html, "9999"));
    }

    [Fact]
    public void ParseTorrentsFromPage_EmptyHtml_ReturnsEmpty()
    {
        Assert.Empty(KinozalParser.ParseTorrentsFromPage("", "8"));
        Assert.Empty(KinozalParser.ParseTorrentsFromPage("<html></html>", "8"));
    }

    [Fact]
    public void ParseInfoHash_FlareSolverrFragment_ReturnsLabeledHash()
    {
        const string html = "<html><head></head><body><ul><li>Инфо хеш: 7C4FCE77B05BC2711C8445C0B1E47011CF4214CE</li>"
            + "<li>Размер части торрента: 1 МБ</li></ul></body></html>";

        Assert.Equal("7C4FCE77B05BC2711C8445C0B1E47011CF4214CE", KinozalParser.ParseInfoHash(html));
    }

    [Fact]
    public void ParseInfoHash_BrowseSizedHtml_DoesNotUseLooseHex()
    {
        string html = new string('x', 9000) + "0123456789abcdef0123456789abcdef01234567";
        Assert.Null(KinozalParser.ParseInfoHash(html));
    }

    [Fact]
    public void ParseInfoHash_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(KinozalParser.ParseInfoHash(null));
        Assert.Null(KinozalParser.ParseInfoHash(""));
        Assert.Null(KinozalParser.ParseInfoHash("Торрент файл не найден."));
    }

    [Fact]
    public void IsTransientBrowseFailure_NullAndNginx503()
    {
        Assert.True(KinozalParser.IsTransientBrowseFailure(null));
        Assert.True(KinozalParser.IsTransientBrowseFailure(""));
        Assert.True(KinozalParser.IsTransientBrowseFailure(
            "<html><head><title>503 Service Temporarily Unavailable</title></head></html>"));
        Assert.True(KinozalParser.IsTransientBrowseFailure("<title>Just a moment...</title>"));
        Assert.False(KinozalParser.IsTransientBrowseFailure(FixtureLoader.Read("Kinozal/browse_c22.html")));
    }

    [Fact]
    public void IsLoginWall_AndLoggedIn_FromFixture()
    {
        string listing = FixtureLoader.Read("Kinozal/browse_c22.html");
        Assert.True(KinozalParser.IsLoggedIn(listing));
        Assert.False(KinozalParser.IsLoginWall(listing));
        Assert.True(KinozalParser.IsLoginWall("<form action=\"/takelogin.php\"><input name=\"username\">"));
        Assert.False(KinozalParser.IsLoginWall("<title>503 Service Temporarily Unavailable</title>"));
    }

    [Fact]
    public void ParseTorrentsFromPage_ChromiumDoubleQuotedRows_YieldsTorrents()
    {
        string html = FixtureLoader.Read("Kinozal/browse_chromium_quoted.html");
        List<TorrentDetails> torrents = KinozalParser.ParseTorrentsFromPage(html, "8");

        Assert.Equal(2, torrents.Count);
        Assert.All(torrents, t =>
        {
            Assert.Contains("/details.php?id=", t.url, StringComparison.Ordinal);
            Assert.DoesNotContain("userdetails", t.url, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal($"{AppInit.conf.Kinozal.host.TrimEnd('/')}/details.php?id=2153071", torrents[0].url);
        Assert.Equal($"{AppInit.conf.Kinozal.host.TrimEnd('/')}/details.php?id=2153065", torrents[1].url);
        Assert.All(torrents, t =>
        {
            Assert.Equal("kinozal", t.trackerName);
            Assert.Equal(new[] { "movie" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
            Assert.NotEqual(default, t.createTime);
        });
        Assert.Equal("Молодожены", torrents[0].name);
        Assert.Equal("Just Married", torrents[0].originalname);
        Assert.Equal(2003, torrents[0].relased);
    }

    [Fact]
    public void HasTorrentListingLinks_IgnoresUserdetails()
    {
        Assert.False(KinozalParser.HasTorrentListingLinks(
            "<a href=\"/userdetails.php?id=191355\">profile</a><a href=\"#\">Выход</a>"));
        Assert.True(KinozalParser.HasTorrentListingLinks(
            "<a href=\"/details.php?id=2153071\" class=\"r0\">title</a>"));
        Assert.False(KinozalParser.TryGetDetailsId("https://kinozal.guru/userdetails.php?id=191355", out _));
        Assert.False(KinozalParser.TryGetDetailsId("https://kinozal.guru/userdetails.php?id=2153071", out _));
        Assert.True(KinozalParser.TryGetDetailsId("https://kinozal.guru/details.php?id=2153071", out int detailsId));
        Assert.Equal(2153071, detailsId);
        Assert.Equal(2, KinozalParser.CountTorrentListingLinks(
            FixtureLoader.Read("Kinozal/browse_chromium_quoted.html")));
        Assert.False(KinozalParser.HasTorrentListingLinks(null));
    }

    [Fact]
    public void ParseTorrentsFromPage_UserdetailsFirstInRow_StoresDetailsUrl()
    {
        const string html =
            "<title>Раздачи :: Кинозал.GURU</title>"
            + "<table class=\"t_peer\"><tr class=\"bg\">"
            + "<td class=\"sl\"><a href=\"/userdetails.php?id=191355\" class=\"u6\">uploader</a></td>"
            + "<td class=\"nam\"><a href=\"/details.php?id=2153071\" class=\"r0\">"
            + "Молодожены / Just Married / 2003 / ДБ / BDRip (720p)</a></td>"
            + "<td class=\"s\">0</td>"
            + "<td class=\"s\">7.11 ГБ</td>"
            + "<td class=\"sl_s\">1</td>"
            + "<td class=\"sl_p\">2</td>"
            + "<td class=\"s\">16.07.2024 в 12:00</td>"
            + "</tr></table>";

        var torrents = KinozalParser.ParseTorrentsFromPage(html, "8");
        Assert.Single(torrents);
        Assert.Equal($"{AppInit.conf.Kinozal.host.TrimEnd('/')}/details.php?id=2153071", torrents[0].url);
        Assert.DoesNotContain("userdetails", torrents[0].url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Молодожены", torrents[0].name);
    }

    [Fact]
    public void ParseTorrentsFromPage_UserdetailsOnlyRow_ReturnsEmpty()
    {
        const string html =
            "<table class=\"t_peer\"><tr class=\"bg\">"
            + "<td class=\"nam\"><a href=\"/userdetails.php?id=191355\" class=\"r0\">uploader</a></td>"
            + "<td class=\"s\">0</td><td class=\"s\">7.11 ГБ</td>"
            + "<td class=\"sl_s\">1</td><td class=\"sl_p\">2</td>"
            + "<td class=\"s\">16.07.2024 в 12:00</td>"
            + "</tr></table>";

        Assert.Empty(KinozalParser.ParseTorrentsFromPage(html, "8"));
    }

    [Fact]
    public void IsValidBrowsePage_RequiresTPeerAndKinozalTitle()
    {
        string listing = FixtureLoader.Read("Kinozal/browse_c8.html");
        Assert.True(KinozalParser.IsValidBrowsePage(listing));
        Assert.True(KinozalParser.IsValidBrowsePage(FixtureLoader.Read("Kinozal/browse_chromium_quoted.html")));
        Assert.False(KinozalParser.IsValidBrowsePage(
            "<title>RuTracker.org :: forum</title><a href=\"/userdetails.php?id=1\">x</a>"));
        Assert.False(KinozalParser.IsValidBrowsePage(
            "<title>Раздачи :: Кинозал.GURU</title><a href=\"#\">Выход</a>"));
    }

    [Fact]
    public void FormatBrowseDiag_LengthTPeerTitle_NoBody()
    {
        const string shell =
            "<title>Раздачи :: Кинозал.GURU</title>"
            + "<a href=\"/userdetails.php?id=191355\">profile</a>"
            + "<a href=\"#\">Выход</a>";

        string diag = KinozalParser.FormatBrowseDiag(shell);
        Assert.Contains("len=", diag);
        Assert.Contains("t_peer=False", diag);
        Assert.Contains("Кинозал.GURU", diag);
        Assert.DoesNotContain("userdetails", diag);

        Assert.Equal("len=0", KinozalParser.FormatBrowseDiag(null));
        Assert.Contains("t_peer=True", KinozalParser.FormatBrowseDiag(FixtureLoader.Read("Kinozal/browse_c22.html")));
    }

    [Fact]
    public void UpdateTasksParseDelayMs_CapsAtTwoSeconds()
    {
        Assert.Equal(0, KinozalParser.UpdateTasksParseDelayMs(0));
        Assert.Equal(1500, KinozalParser.UpdateTasksParseDelayMs(1500));
        Assert.Equal(2000, KinozalParser.UpdateTasksParseDelayMs(10000));
        Assert.Equal(0, KinozalParser.UpdateTasksParseDelayMs(-5));
    }

    [Fact]
    public void IsStaleListingHtml_LoggedInShellWithoutTPeer()
    {
        const string shell =
            "<title>Раздачи :: Кинозал.GURU</title>"
            + "<a href=\"/userdetails.php?id=191355\">profile</a>"
            + "<a href=\"#\">Выход</a>";

        Assert.True(KinozalParser.IsLoggedIn(shell));
        Assert.True(KinozalParser.IsStaleListingHtml(shell));
        Assert.False(KinozalParser.IsValidBrowsePage(shell));
        Assert.False(KinozalParser.IsLoginWall(shell));
        Assert.False(KinozalParser.IsStaleListingHtml(FixtureLoader.Read("Kinozal/browse_c22.html")));
        Assert.False(KinozalParser.IsStaleListingHtml("<title>Just a moment...</title>"));
        Assert.False(KinozalParser.IsStaleListingHtml("<form action=\"/takelogin.php\"><input name=\"username\">"));
    }

    [Fact]
    public void IsEmptySearchResult_NotStale_MarksPageDone()
    {
        string empty = FixtureLoader.Read("Kinozal/browse_empty_search.html");
        Assert.True(KinozalParser.IsLoggedIn(empty));
        Assert.True(KinozalParser.IsEmptySearchResult(empty));
        Assert.False(KinozalParser.IsStaleListingHtml(empty));
        Assert.False(KinozalParser.IsValidBrowsePage(empty));
        Assert.False(KinozalParser.IsLoginWall(empty));
        Assert.True(KinozalParser.ShouldMarkPageDone(0, 0, KinozalParser.CountTorrentListingLinks(empty)));
        Assert.False(KinozalParser.IsEmptySearchResult(FixtureLoader.Read("Kinozal/browse_c22.html")));
        Assert.False(KinozalParser.IsEmptySearchResult(
            "<title>Раздачи :: Кинозал.GURU</title><a href=\"#\">Выход</a>"));
    }

    [Fact]
    public void BrowseFiltersMismatch_SelectedCatAndYear()
    {
        string listing = FixtureLoader.Read("Kinozal/browse_c22.html");
        Assert.True(KinozalParser.TryGetSelectedBrowseFilter(listing, "c", out string cat) && cat == "22");
        Assert.False(KinozalParser.BrowseFiltersMismatch(listing, "22", null));
        Assert.True(KinozalParser.BrowseFiltersMismatch(listing, "13", null));
        Assert.False(KinozalParser.BrowseFiltersMismatch(listing, "22", "&d=2020&t=1"));

        string empty = FixtureLoader.Read("Kinozal/browse_empty_search.html");
        Assert.False(KinozalParser.BrowseFiltersMismatch(empty, "13", "&d=2020&t=1"));
        Assert.True(KinozalParser.BrowseFiltersMismatch(empty, "15", "&d=2020&t=1"));
        Assert.True(KinozalParser.BrowseFiltersMismatch(empty, "13", "&d=2021&t=1"));
        Assert.False(KinozalParser.BrowseFiltersMismatch(
            "<title>Кинозал.GURU</title><a href=\"#\">Выход</a>", "13", "&d=2020&t=1"));

        const string allYears =
            "<select name=\"d\"><option selected=selected value=0>все года</option></select>"
            + "<select name=\"c\"><option selected=selected value=13>x</option></select>";
        Assert.False(KinozalParser.BrowseFiltersMismatch(allYears, "13", "&d=2020&t=1"));
    }

    [Fact]
    public void YearTaskPageCount_PagerDigitIsExclusiveUpperBound()
    {
        Assert.Equal(1, KinozalParser.YearTaskPageCount(0));
        Assert.Equal(1, KinozalParser.YearTaskPageCount(-1));
        Assert.Equal(15, KinozalParser.YearTaskPageCount(15));
        Assert.Equal(1, KinozalParser.YearTaskPageCount(""));
        Assert.Equal(15, KinozalParser.YearTaskPageCount(
            "<li><a href=\"?c=45&amp;page=14\">15</a></li><li><a rel=\"next\" href=\"?page=15\">Вперед</a>"));
        Assert.Equal(100, KinozalParser.YearTaskPageCount(FixtureLoader.Read("Kinozal/browse_c22.html")));
    }

    [Fact]
    public void PrunePagesBeyondYearCount_DropsInclusiveTail()
    {
        var pages = new List<TaskParse> { new(0), new(8), new(9), new(10) };
        Assert.Equal(2, KinozalParser.PrunePagesBeyondYearCount(pages, 9));
        Assert.Equal(new[] { 0, 8 }, pages.Select(p => p.page).ToArray());
        Assert.Equal(0, KinozalParser.PrunePagesBeyondYearCount(pages, 9));
        Assert.Equal(0, KinozalParser.PrunePagesBeyondYearCount(null, 9));
    }

    [Fact]
    public void ShouldMarkPageDone_EmptyOrFullyResolved()
    {
        Assert.True(KinozalParser.ShouldMarkPageDone(0, 0, 0));
        Assert.False(KinozalParser.ShouldMarkPageDone(0, 0, 50));
        Assert.True(KinozalParser.ShouldMarkPageDone(10, 10, 10));
        Assert.False(KinozalParser.ShouldMarkPageDone(10, 0, 10));
        Assert.False(KinozalParser.ShouldMarkPageDone(10, 9, 10));
    }

    [Fact]
    public void DryRun_AllFixtures_ReportParseRates()
    {
        foreach (object[] row in FixtureCases())
        {
            string cat = (string)row[0];
            string file = (string)row[1];
            string[] types = (string[])row[2];
            string html = FixtureLoader.Read($"Kinozal/{file}");
            var torrents = KinozalParser.ParseTorrentsFromPage(html, cat);
            int withYear = torrents.Count(t => t.relased > 0);
            int withOriginal = torrents.Count(t => !string.IsNullOrWhiteSpace(t.originalname));
            _output.WriteLine(
                $"DRY-RUN cat={cat,-3} types=[{string.Join(",", types)}] " +
                $"parsed={torrents.Count,2} withYear={withYear,2} withOriginal={withOriginal,2}");
            Assert.True(torrents.Count >= 40, $"dry-run cat {cat}: low yield {torrents.Count}");
        }
    }
}
