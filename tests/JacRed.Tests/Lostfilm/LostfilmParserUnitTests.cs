using JacRed.Infrastructure.Trackers.Lostfilm;
using JacRed.Models.Details;
using Xunit;

namespace JacRed.Tests.Lostfilm;

public class LostfilmPlayEpisodeIdTests
{
    [Fact]
    public void PreferLongCombinedId()
    {
        string html = FixtureLoader.Read("Lostfilm/episode_play_long_id.html");
        Assert.Equal("780002005", LostfilmParser.TryExtractPlayEpisodeId(html));
    }

    [Fact]
    public void BuildsIdFromThreeArgForm()
    {
        string html = FixtureLoader.Read("Lostfilm/episode_play_three_arg.html");
        Assert.Equal("780002005", LostfilmParser.TryExtractPlayEpisodeId(html));
    }

    [Theory]
    [InlineData("PlayEpisode('12','3','4')", "12003004")]
    [InlineData("PlayEpisode(\"99\",\"1\",\"10\")", "99001010")]
    [InlineData("PlayMovie('1234567')", "1234567")]
    [InlineData("PlayEpisode('55','0','1')", "55000001")]
    public void MovieOrEpisode_ExtractsExpected(string snippet, string expected)
    {
        Assert.Equal(expected, LostfilmParser.TryExtractPlayMovieOrEpisodeId($"<script>{snippet}</script>"));
    }

    [Fact]
    public void EmptyHtml_ReturnsNull()
    {
        Assert.Null(LostfilmParser.TryExtractPlayEpisodeId(null));
        Assert.Null(LostfilmParser.TryExtractPlayEpisodeId(""));
        Assert.Null(LostfilmParser.TryExtractPlayEpisodeId("<html></html>"));
    }
}

public class LostfilmParserHelperTests
{
    [Theory]
    [InlineData("1080", true)]
    [InlineData("1080p", true)]
    [InlineData("2160", true)]
    [InlineData("2160p", true)]
    [InlineData("720p", false)]
    [InlineData("SD", false)]
    [InlineData("1440p", false)]
    public void IsPreferredQuality_1080And2160(string quality, bool expected)
    {
        Assert.Equal(expected, LostfilmParser.IsPreferredQuality(quality));
    }

    [Theory]
    [InlineData("1080", "1080p")]
    [InlineData("2160", "2160p")]
    [InlineData("720", "720p")]
    [InlineData("sd", "SD")]
    [InlineData("mp4", "720p")]
    [InlineData("2160p", "2160p")]
    public void NormalizeQuality(string input, string expected)
    {
        Assert.Equal(expected, LostfilmParser.NormalizeQuality(input));
    }

    [Fact]
    public void ShortenSeriesName_StripsSerailBoilerplate()
    {
        string og = "Капли Бога (Drops of God). Сериал Капли Бога канал: гид";
        Assert.Equal("Капли Бога", LostfilmParser.ShortenSeriesName(og));
    }

    [Fact]
    public void HasRuName_DetectsXxBucket()
    {
        Assert.False(LostfilmParser.HasRuName("Ponies", "Ponies"));
        Assert.True(LostfilmParser.IsXxNameBucket("Ponies", "Ponies"));
        Assert.True(LostfilmParser.HasRuName("Пони", "Ponies"));
    }

    [Fact]
    public void ApplyMagnetCache_DoesNotOverwriteTitle_PrefersRuNames()
    {
        var incoming = new TorrentDetails
        {
            url = "https://www.lostfilm.tv/series/X/season_1/episode_1/#1080p",
            title = "Пони / Ponies / 1 сезон 1 серия [2026, 1080p]",
            name = "Ponies",
            originalname = "Ponies"
        };
        var cached = new TorrentDetails
        {
            magnet = "magnet:?xt=urn:btih:ABC",
            title = "OLD TITLE",
            sizeName = "1 GB",
            name = "Пони",
            originalname = "Ponies"
        };

        LostfilmParser.ApplyMagnetCache(incoming, cached);

        Assert.Equal("magnet:?xt=urn:btih:ABC", incoming.magnet);
        Assert.Equal("1 GB", incoming.sizeName);
        Assert.Equal("Пони / Ponies / 1 сезон 1 серия [2026, 1080p]", incoming.title);
        Assert.Equal("Пони", incoming.name);
        Assert.Equal("Ponies", incoming.originalname);
    }

    [Fact]
    public void CloneWithQuality_UsesHashSuffix()
    {
        var src = new TorrentDetails
        {
            trackerName = "lostfilm",
            types = new[] { "serial" },
            url = "https://www.lostfilm.tv/series/X/season_1/episode_1/",
            title = "Имя / Name / 1 сезон 1 серия [2026]",
            name = "Имя",
            originalname = "Name",
            relased = 2026
        };
        var clone = LostfilmParser.CloneWithQuality(src, "magnet:?xt=urn:btih:1", "1080", "2 GB");
        Assert.Equal("https://www.lostfilm.tv/series/X/season_1/episode_1/#1080p", clone.url);
        Assert.Contains("1080p", clone.title);
        Assert.Equal("magnet:?xt=urn:btih:1", clone.magnet);
        Assert.True(LostfilmParser.HasRuName(clone));
    }

    [Fact]
    public void StableUrlId_IncludesQuality_HostIndependent()
    {
        int a = LostfilmParser.StableUrlId("https://www.lostfilm.tv/series/X/season_1/episode_1/#1080p");
        int b = LostfilmParser.StableUrlId("https://mirror.example/series/X/season_1/episode_1/#1080p");
        int c = LostfilmParser.StableUrlId("https://www.lostfilm.tv/series/X/season_1/episode_1/#720p");
        Assert.True(a > 0);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ParseVPageQualityLinkUrls_Only1080And2160()
    {
        string html = FixtureLoader.Read("Lostfilm/v_page_qualities.html");
        var links = LostfilmParser.ParseVPageQualityLinkUrls(html);
        Assert.Equal(2, links.Count);
        Assert.Contains(links, x => x.quality == "1080p");
        Assert.Contains(links, x => x.quality == "2160p");
        Assert.All(links, x => Assert.True(LostfilmParser.IsPreferredQuality(x.quality)));
    }
}
