using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Trackers.Ultradox;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Ultradox;

/// <summary>
/// Regression against Go testdata from ultradox.onl (2026 domain move).
/// Seed: temp/jacred-go/cron/ultradox/testdata/
/// Refresh: python3 scripts/dry_run_ultradox_parser.py --refresh-fixtures
/// </summary>
public class UltradoxParserFixtureTests
{
    readonly ITestOutputHelper _output;
    const string Host = "https://ultradox.onl";

    public UltradoxParserFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _ = AppInit.conf.Ultradox.host;
    }

    [Fact]
    public void Sections_AndReferer_MatchGo()
    {
        Assert.Equal(6, UltradoxCategories.Map.Count);
        Assert.Equal("ultradox", UltradoxParser.TrackerName);
        Assert.True(UltradoxCategories.Map.ContainsKey("serial-hd"));
        Assert.Contains("google.", UltradoxParser.SearchEngineReferer);
    }

    [Fact]
    public void ParseListingHtml_Fixture_Has18Rows()
    {
        string html = FixtureLoader.Read("Ultradox/listing_serial-hd.html");
        var items = UltradoxParser.ParseListingHtml(html);

        _output.WriteLine($"rows={items.Count}");
        Assert.Equal(18, items.Count);

        Assert.Equal("/serial-hd/54741-jejforija-3-sezon.html", items[0].DetailUrl);
        Assert.Equal("Эйфория (3 сезон) [+9 серия] [Ultradox]", items[0].Title);
        Assert.Equal("tt8772296", items[0].Imdb);
        Assert.NotEqual(default, items[0].CreateTime);

        Assert.All(items, it =>
        {
            Assert.False(string.IsNullOrWhiteSpace(it.Title));
            Assert.StartsWith("/", it.DetailUrl);
            Assert.DoesNotContain("magnet:", it.Title, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ListingMagnets_AreStillPlaceholders()
    {
        string html = FixtureLoader.Read("Ultradox/listing_serial-hd.html");
        Assert.True(UltradoxParser.ListingMagnetsArePlaceholders(html));
    }

    [Fact]
    public void TryParseDetailHtml_ThreeQualityVariants()
    {
        string html = FixtureLoader.Read("Ultradox/detail_serial.html");
        Assert.True(UltradoxParser.TryParseDetailHtml(html, out var variants, out var info));

        Assert.Equal(3, variants.Count);
        Assert.Equal(2026, info.Year);
        Assert.Equal("Euphoria", info.Original);

        var qualities = variants.Select(v => v.Quality).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("1080p", qualities);
        Assert.Contains("720p", qualities);
        Assert.Contains("400p", qualities);

        Assert.All(variants, v =>
        {
            Assert.Equal(40, v.Hash.Length);
            Assert.False(string.IsNullOrWhiteSpace(v.Magnet));
            Assert.Contains("magnet:?xt=urn:btih:", v.Magnet, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("btih:&", v.Magnet, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ExtractDetailYear_Fixture_Is2026()
    {
        string html = FixtureLoader.Read("Ultradox/detail_serial.html");
        Assert.Equal(2026, UltradoxParser.ExtractDetailYear(html));
    }

    [Theory]
    [InlineData("02-04-2025, 14:32", false)]
    [InlineData("Сегодня, 10:06", false)]
    [InlineData("Вчера, 22:05", false)]
    [InlineData("позавчера", true)]
    [InlineData("", true)]
    public void ParseRowDate_Shapes(string input, bool wantZero)
    {
        DateTime got = UltradoxParser.ParseRowDate(input);
        if (wantZero)
            Assert.Equal(default, got);
        else
            Assert.NotEqual(default, got);
    }

    [Theory]
    [InlineData("Ип Ман: Битва кланов (2026) (ПМ) [BDRip]", "Ип Ман: Битва кланов", "", 2026)]
    [InlineData("30 ночей с бывшим (2025) (Дубляж [Чистый звук]) [BDRip]", "30 ночей с бывшим", "", 2025)]
    [InlineData("Мотор Сити (Автомобильный город) (2025) (Дубляж) [Telecine]", "Мотор Сити (Автомобильный город)", "", 2025)]
    [InlineData("Трасса «Море - море» (2026) (Оригинал) [Telecine]", "Трасса «Море - море»", "", 2026)]
    [InlineData("Эйфория (3 сезон) [+9 серия] [Ultradox]", "Эйфория", "", 0)]
    [InlineData("Боевой петух (1 сезон) [+12 серия] (ПМ) [WEB-DL]", "Боевой петух", "", 0)]
    [InlineData("Триган: Наблюдая за звёздами (2 сезон) [+12 серия] [Ultradox]", "Триган: Наблюдая за звёздами", "", 0)]
    [InlineData("Проект Пуля/Пуля (1 сезон) [+12 серия] [Ultradox]", "Проект Пуля/Пуля", "", 0)]
    [InlineData("Губка Боб квадратные штаны (17 сезон) [+6 серия] [Ultradox]", "Губка Боб квадратные штаны", "", 0)]
    [InlineData("Некий сериал [+5 серия] [Ultradox]", "Некий сериал", "", 0)]
    [InlineData("Астрид и Рафаэлла / Astrid et Raphaëlle (2025) [WEB-DL]", "Астрид и Рафаэлла", "Astrid et Raphaëlle", 2025)]
    public void ParseTitle_MatchesGoCases(string title, string wantName, string wantOrig, int wantYear)
    {
        var (name, original, year) = UltradoxParser.ParseTitle(title);
        Assert.Equal(wantName, name);
        Assert.Equal(wantOrig, original);
        Assert.Equal(wantYear, year);
    }

    [Fact]
    public void EpisodeCounter_DoesNotChangeIdentity()
    {
        var (before, _, _) = UltradoxParser.ParseTitle("Эйфория (3 сезон) [+9 серия] [Ultradox]");
        var (after, _, _) = UltradoxParser.ParseTitle("Эйфория (3 сезон) [+10 серия] [Ultradox]");
        Assert.Equal(before, after);
        Assert.Equal("Эйфория", before);
    }

    [Theory]
    [InlineData("Euphoria.US.S03.1080p.Ru.Ultradox.torrent", "Euphoria")]
    [InlineData("Taakstraf.S01.1080p.Ru.Ultradox.torrent", "Taakstraf")]
    [InlineData("SpongeBob.SquarePants.S17.720p.Ru.Ultradox.torrent", "SpongeBob SquarePants")]
    [InlineData("Trigun.Stargaze.S02.720p.Ru.Ultradox.torrent", "Trigun Stargaze")]
    [InlineData("Shumatsu.no.Valkyrie.S03.1080p.Ultradox.torrent", "Shumatsu no Valkyrie")]
    [InlineData(
        "Life.Larry.and.the.Pursuit.of.Unhappiness.An.Almost.History.of.America.S01.1080p.Ru.Ultradox.torrent",
        "Life Larry and the Pursuit of Unhappiness An Almost History of America")]
    [InlineData("The.Death.Of.Robin.Hood.2026.D.BDRip.avi.torrent", "The Death Of Robin Hood")]
    [InlineData("Yellow.Letters.2026.Pm.BDRip.1O8Op.mkv.torrent", "Yellow Letters")]
    [InlineData("30.Notti.con.il.mio.ex.2025.D.BDRip.avi.torrent", "30 Notti con il mio ex")]
    [InlineData("Game.of.Shark.2024.Pk.WEB-DL.1O8Op.mkv.torrent", "Game of Shark")]
    [InlineData(
        "State.of.Ramadhani.Dharyu.Dhani.Nu.Thay.2026.Pk.TELECINE.avi.torrent",
        "State of Ramadhani Dharyu Dhani Nu Thay")]
    [InlineData("Trassa.more.more.2026O.TELECINE.1O8Op.mkv.torrent", "Trassa more more")]
    [InlineData("S01.1080p.Ru.Ultradox.torrent", "")]
    [InlineData("2026.D.BDRip.torrent", "")]
    [InlineData("", "")]
    public void OriginalFromFilename_MatchesGo(string dn, string want)
    {
        Assert.Equal(want, UltradoxParser.OriginalFromFilename(dn));
    }

    [Fact]
    public void Original_IsStableAcrossVariants()
    {
        string[][] groups =
        {
            new[]
            {
                "30.Notti.con.il.mio.ex.2025.D.BDRip.avi.torrent",
                "30.Notti.con.il.mio.ex.2025.D.BDRip.1O8Op.mkv.torrent"
            },
            new[]
            {
                "Svoya.v.dosku.2026.O.WEB-DLRip.avi.torrent",
                "Svoya.v.dosku.2026.O.WEB-DL.1O8Op.mkv.torrent"
            },
            new[]
            {
                "Euphoria.US.S03.1080p.Ru.Ultradox.torrent",
                "Euphoria.US.S03.720p.Ru.Ultradox.torrent",
                "Euphoria.US.S03.400p.Ru.Ultradox.torrent"
            },
        };

        foreach (string[] g in groups)
        {
            string first = UltradoxParser.OriginalFromFilename(g[0]);
            foreach (string dn in g.Skip(1))
                Assert.Equal(first, UltradoxParser.OriginalFromFilename(dn));
        }
    }

    [Fact]
    public void QualityVariants_ShareBucketIdentity_DistinctUrls()
    {
        var item = new UltradoxListingItem
        {
            Title = "Эйфория (3 сезон) [+9 серия] [Ultradox]",
            DetailUrl = "/serial-hd/54741-jejforija-3-sezon.html",
            CreateTime = DateTime.UtcNow
        };
        var info = new UltradoxDetailInfo { Year = 2026, Original = "Euphoria" };
        var variants = new[]
        {
            new UltradoxMagnetVariant
            {
                Hash = "0474f44b58fbec31ec145d610a74488a8231f214",
                Magnet = "magnet:?a", Bytes = 21648023723, Dn = "x.1080p.torrent", Quality = "1080p"
            },
            new UltradoxMagnetVariant
            {
                Hash = "e89466561abc2312894f75d39e6783d4712fa0e4",
                Magnet = "magnet:?b", Bytes = 13940626498, Dn = "x.720p.torrent", Quality = "720p"
            },
            new UltradoxMagnetVariant
            {
                Hash = "19ca78e954b198c8c86d5b7f76cf1aa625514e3e",
                Magnet = "magnet:?c", Bytes = 8619991040, Dn = "x.400p.torrent", Quality = "400p"
            },
        };

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in variants)
        {
            var rec = UltradoxParser.BuildTorrent(Host, "serial-hd", new[] { "serial" }, item, v, info);
            Assert.NotNull(rec);
            Assert.Equal("Эйфория", rec.name);
            Assert.Equal("Euphoria", rec.originalname);
            Assert.Equal(2026, rec.relased);
            Assert.Equal(1, rec.sid);
            Assert.Equal(1, rec.pir);
            Assert.Contains(v.Quality, rec.title, StringComparison.OrdinalIgnoreCase);
            keys.Add(rec.name + "|" + rec.originalname);
            urls.Add(rec.url);
        }

        Assert.Single(keys);
        Assert.Equal(3, urls.Count);
    }

    [Fact]
    public void Rufilm_KeepsOriginalEmpty()
    {
        var item = new UltradoxListingItem
        {
            Title = "Своя в доску (2026) (Оригинал) [WEB-DL]",
            DetailUrl = "/rufilm/1-x.html",
            CreateTime = DateTime.UtcNow
        };
        var variant = new UltradoxMagnetVariant
        {
            Hash = "abc1234567890",
            Magnet = "magnet:?x",
            Dn = "Svoya.v.dosku.2026.O.WEB-DL.1O8Op.mkv.torrent",
            Quality = "1080p"
        };
        var info = new UltradoxDetailInfo { Year = 2026, Original = "Svoya v dosku" };

        var ru = UltradoxParser.BuildTorrent(Host, "rufilm", new[] { "movie" }, item, variant, info);
        Assert.Equal("", ru.originalname);

        var hd = UltradoxParser.BuildTorrent(Host, "hd", new[] { "movie" }, item, variant, info);
        Assert.Equal("Svoya v dosku", hd.originalname);
    }

    [Fact]
    public void ParseListingHtml_Empty_ReturnsEmpty()
    {
        Assert.Empty(UltradoxParser.ParseListingHtml(""));
        Assert.Empty(UltradoxParser.ParseListingHtml("<html></html>"));
    }
}
