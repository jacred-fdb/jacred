using System;
using System.IO;
using JacRed.Infrastructure.Trackers.Anibelka;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Anibelka;

/// <summary>
/// Regression against Go testdata captured anonymously from anibelka.com (2026-07-25).
/// Seed: temp/jacred-go/cron/anibelka/testdata/
/// Refresh: python3 scripts/dry_run_anibelka_parser.py --refresh-fixtures
/// </summary>
public class AnibelkaParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string Host = "https://anibelka.com";

    public AnibelkaParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Anibelka.host;
    }

    [Fact]
    public void Sections_AreAnimeOnly()
    {
        Assert.Equal(5, AnibelkaCategories.Map.Count);
        Assert.True(AnibelkaCategories.Map.ContainsKey("33"));
        Assert.Equal(new[] { "anime" }, AnibelkaCategories.Map["33"].Types);
        Assert.Equal("anibelka", AnibelkaParser.TrackerName);
    }

    [Fact]
    public void ParseListingHtml_Fixture_SkipsServiceTopics()
    {
        string html = FixtureLoader.Read("Anibelka/forum_f33.html");
        var items = AnibelkaParser.ParseListingHtml(html);

        _output.WriteLine($"topics={items.Count}");
        Assert.True(items.Count > 0, "no topics parsed");
        Assert.All(items, it =>
        {
            Assert.False(string.IsNullOrWhiteSpace(it.TopicId));
            Assert.StartsWith("[", it.Title);
        });

        Assert.Equal("1849", items[0].TopicId);
        Assert.Equal(
            "[rus] Фермерская жизнь в ином мире / Isekai Nonbiri Nouka [2TV][2023-2026, повседневность, фэнтези]",
            items[0].Title);
    }

    [Fact]
    public void ParseTopicHtml_RusFixture_PicksTorrentNotPoster()
    {
        string html = FixtureLoader.Read("Anibelka/topic_rus.html");
        Assert.True(AnibelkaParser.TryParseTopicHtml(html, out var info));

        Assert.Equal("7316", info.TorrentId);
        Assert.Equal("4.75 ГБ", info.SizeName);
        Assert.Equal(5, info.Sid);
        Assert.Equal(0, info.Pir);
        Assert.Equal(new DateTime(2026, 7, 23, 5, 56, 0, DateTimeKind.Utc), info.CreateTime);
    }

    [Fact]
    public void ParseTopicHtml_FeatureFilmFixture_HasTorrent()
    {
        string html = FixtureLoader.Read("Anibelka/topic_mv.html");
        Assert.True(AnibelkaParser.TryParseTopicHtml(html, out var info));
        Assert.False(string.IsNullOrWhiteSpace(info.TorrentId));
        Assert.False(string.IsNullOrWhiteSpace(info.SizeName));
        _output.WriteLine($"torrentId={info.TorrentId} size={info.SizeName}");
    }

    [Theory]
    [InlineData(
        "[rus] Фермерская жизнь в ином мире / Isekai Nonbiri Nouka [2TV][2023-2026, повседневность]",
        "Фермерская жизнь в ином мире", "Isekai Nonbiri Nouka", 2023)]
    [InlineData(
        "[mv] Вторая страна / Ni no Kuni [R,S][2019, приключения, фэнтези]",
        "Вторая страна", "Ni no Kuni", 2019)]
    [InlineData(
        "[uni] Вампир не умеет правильно сосать / Chanto Suenai Kyuuketsuki-chan / Li'l Miss Vampire [TV][2024, комедия]",
        "Вампир не умеет правильно сосать", "Chanto Suenai Kyuuketsuki-chan", 2024)]
    [InlineData(
        "[rus] Туалетный мальчик Ханако / Ханако после школы / Jibaku Shounen Hanako-kun / Houkago Shounen Hanako-kun [TV][2020, мистика]",
        "Туалетный мальчик Ханако", "Jibaku Shounen Hanako-kun", 2020)]
    [InlineData(
        "[uni] P-15 / R-15 [TV+OVA][2011, комедия, школа, этти]",
        "P-15", "R-15", 2011)]
    [InlineData("[psp] Хёка / Hyouka", "Хёка", "Hyouka", 0)]
    public void ParseTitle_MatchesGoCases(string title, string wantName, string wantOrig, int wantYear)
    {
        var (name, original, year) = AnibelkaParser.ParseTitle(title);
        Assert.Equal(wantName, name);
        Assert.Equal(wantOrig, original);
        Assert.Equal(wantYear, year);
    }

    [Fact]
    public void ParseRuDate_MoscowToUtc()
    {
        DateTime got = AnibelkaParser.ParseRuDate("23 июл 2026, 08:56");
        Assert.Equal(new DateTime(2026, 7, 23, 5, 56, 0, DateTimeKind.Utc), got);

        Assert.Equal(default, AnibelkaParser.ParseRuDate("вчера"));
        Assert.Equal(default, AnibelkaParser.ParseRuDate(""));
        Assert.Equal(default, AnibelkaParser.ParseRuDate("32 abc 2026"));

        foreach (string mon in new[] { "янв", "фев", "мар", "апр", "май", "июн", "июл", "авг", "сен", "окт", "ноя", "дек" })
            Assert.NotEqual(default, AnibelkaParser.ParseRuDate($"01 {mon} 2026, 00:00"));
    }

    [Fact]
    public void LastPageFromHtml_Fixture_Is40()
    {
        string html = FixtureLoader.Read("Anibelka/forum_f33.html");
        Assert.Equal(40, AnibelkaParser.LastPageFromHtml(html));
        Assert.Equal(0, AnibelkaParser.LastPageFromHtml("<html>no pagination</html>"));
    }

    [Fact]
    public void BuildTorrent_SetsAnimeFields()
    {
        var item = new AnibelkaListingItem
        {
            TopicId = "1849",
            Title = "[rus] Фермерская жизнь в ином мире / Isekai Nonbiri Nouka [2TV][2023-2026, повседневность]"
        };
        var info = new AnibelkaTopicInfo
        {
            TorrentId = "7316",
            SizeName = "4.75 ГБ",
            Sid = 5,
            Pir = 0,
            CreateTime = DateTime.UtcNow
        };
        string magnet = "magnet:?xt=urn:btih:a2e092da06e84fe18b9dc5ca20bf5cc896fceaeb";

        var rec = AnibelkaParser.BuildTorrent(Host, item, info, magnet);
        Assert.NotNull(rec);
        Assert.Equal($"{Host}/viewtopic.php?t=1849", rec.url);
        Assert.Equal("Фермерская жизнь в ином мире", rec.name);
        Assert.Equal("Isekai Nonbiri Nouka", rec.originalname);
        Assert.Equal(5, rec.sid);
        Assert.Equal(new[] { "anime" }, rec.types);
        Assert.Equal("7316", rec.downloadId);

        Assert.Null(AnibelkaParser.BuildTorrent(Host, item, info, ""));
    }

    [Fact]
    public void SyncService_Source_DoesNotReferenceLogin()
    {
        string srcDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "Infrastructure", "Trackers", "Anibelka"));
        string syncPath = Path.Combine(srcDir, "AnibelkaSyncService.cs");
        string parserPath = Path.Combine(srcDir, "AnibelkaParser.cs");

        Assert.True(File.Exists(syncPath), $"missing {syncPath}");
        Assert.True(File.Exists(parserPath), $"missing {parserPath}");

        foreach (string file in new[] { syncPath, parserPath })
        {
            string src = File.ReadAllText(file);
            foreach (string forbidden in new[] { "takeLogin", "ucp.php?mode=login", "Login.U", "Login.P", "cookie:" })
                Assert.DoesNotContain(forbidden, src, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ParseListingHtml_Empty_ReturnsEmpty()
    {
        Assert.Empty(AnibelkaParser.ParseListingHtml(""));
        Assert.Empty(AnibelkaParser.ParseListingHtml("<html></html>"));
    }
}
