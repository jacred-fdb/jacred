using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Xunit;

namespace JacRed.Tests.Indexers;

public class AllohaSearchParamsTests
{
    [Theory]
    [InlineData("tt0137523", "tt0137523")]
    [InlineData("TT0137523", "tt0137523")]
    [InlineData("kp301", "kp301")]
    [InlineData("KP464963", "kp464963")]
    [InlineData("tmdb1315772", "tmdb1315772")]
    [InlineData("tmdb:550", "tmdb550")]
    [InlineData("https://www.themoviedb.org/movie/1315772-minions-monsters", "tmdb1315772")]
    [InlineData("0137523", "tt0137523")]
    public void NormalizeQuery_ExternalIds(string raw, string expected)
    {
        Assert.Equal(expected, IndexerRequestParams.NormalizeQuery(raw));
        Assert.True(IndexerRequestParams.IsImdbOrKpQuery(IndexerRequestParams.NormalizeQuery(raw)));
    }

    [Fact]
    public void NormalizeQuery_PlainTitle_Unchanged()
    {
        Assert.Equal("Fight Club", IndexerRequestParams.NormalizeQuery("Fight Club"));
        Assert.False(IndexerRequestParams.IsImdbOrKpQuery("Fight Club"));
    }

    [Theory]
    [InlineData("1315772", "tmdb1315772")]
    [InlineData("tmdb550", "tmdb550")]
    [InlineData("https://www.themoviedb.org/tv/1399-got", "tmdb1399")]
    public void NormalizeTmdbId(string raw, string expected)
    {
        Assert.Equal(expected, IndexerRequestParams.NormalizeTmdbId(raw));
    }

    [Fact]
    public void NormalizeTmdbId_RejectsImdb()
    {
        Assert.Null(IndexerRequestParams.NormalizeTmdbId("tt0137523"));
    }

    [Fact]
    public void Prowlarr_Movie_BraceTmdbId_BecomesQuery()
    {
        var parsed = ProwlarrQueryParser.Parse("{TmdbId:1315772}", "movie");

        Assert.Equal("tmdb1315772", parsed.TmdbId);
        Assert.Equal("tmdb1315772", parsed.Query);
        Assert.Null(parsed.Title);
    }

    [Fact]
    public void Prowlarr_Tv_BraceTmdbId_BecomesQuery()
    {
        var parsed = ProwlarrQueryParser.Parse("{tmdbid:1399} {Season:1}", "tvsearch");

        Assert.Equal("tmdb1399", parsed.TmdbId);
        Assert.Equal("tmdb1399", parsed.Query);
        Assert.Equal(1, parsed.Season);
    }

    [Fact]
    public void Prowlarr_ImdbAndKp_BraceTokens()
    {
        var imdb = ProwlarrQueryParser.Parse("{ImdbId:tt0137523}", "moviesearch");
        Assert.Equal("tt0137523", imdb.ImdbId);
        Assert.Equal("tt0137523", imdb.Query);

        // KP is not a Prowlarr brace token — plain query still normalizes.
        Assert.Equal("kp361", IndexerRequestParams.NormalizeQuery("kp361"));
        Assert.True(IndexerRequestParams.IsImdbOrKpQuery("kp361"));
    }

    [Fact]
    public void FilterByType_KeepsMatchingAndUntyped()
    {
        var items = new List<Result>
        {
            new() { Title = "a", info = new TorrentInfo { types = new[] { "movie" } } },
            new() { Title = "b", info = new TorrentInfo { types = new[] { "serial" } } },
            new() { Title = "c", info = null },
            new() { Title = "d", info = new TorrentInfo { types = System.Array.Empty<string>() } },
        };

        var filtered = IndexerResultFilters.FilterByType(items, "movie");

        Assert.Equal(3, filtered.Count);
        Assert.Contains(filtered, r => r.Title == "a");
        Assert.Contains(filtered, r => r.Title == "c");
        Assert.Contains(filtered, r => r.Title == "d");
        Assert.DoesNotContain(filtered, r => r.Title == "b");
    }

    [Fact]
    public void FilterByYear_AllowsPlusMinusOne()
    {
        var items = new List<Result>
        {
            new() { Title = "y1999", info = new TorrentInfo { relased = 1999 } },
            new() { Title = "y1998", info = new TorrentInfo { relased = 1998 } },
            new() { Title = "y2001", info = new TorrentInfo { relased = 2001 } },
            new() { Title = "unknown", info = new TorrentInfo { relased = 0 } },
        };

        var filtered = IndexerResultFilters.FilterByYear(items, 1999);

        Assert.Equal(3, filtered.Count);
        Assert.DoesNotContain(filtered, r => r.Title == "y2001");
    }

    [Theory]
    [InlineData("silo S01", "silo")]
    [InlineData("укрытие S01", "укрытие")]
    [InlineData("silo us S01", "silo us")]
    [InlineData("укрытие 2023 S01E01", "укрытие 2023")]
    [InlineData("silo 2023 S01", "silo 2023")]
    [InlineData("укрытие S01E01", "укрытие")]
    [InlineData("укрытие 2023 S01", "укрытие 2023")]
    [InlineData("silo S01E01", "silo")]
    [InlineData("silo 2023 S01E01", "silo 2023")]
    [InlineData("silo us S01E01", "silo us")]
    public void StripSeasonEpisode_SiloQueries(string raw, string expected)
    {
        Assert.Equal(expected, IndexerRequestParams.StripSeasonEpisode(raw));
    }

    [Theory]
    [InlineData("Fight Club 1999")]
    [InlineData("tt14688458")]
    [InlineData("Breaking Bad")]
    public void StripSeasonEpisode_Unchanged_ReturnsNull(string raw)
    {
        Assert.Null(IndexerRequestParams.StripSeasonEpisode(raw));
    }

    [Fact]
    public void StripSeasonEpisode_ChainedWithTrailingYear()
    {
        var strippedSeason = IndexerRequestParams.StripSeasonEpisode("укрытие 2023 S01");
        Assert.Equal("укрытие 2023", strippedSeason);
        Assert.Equal("укрытие", IndexerRequestParams.StripTrailingYear(strippedSeason));
    }
}
