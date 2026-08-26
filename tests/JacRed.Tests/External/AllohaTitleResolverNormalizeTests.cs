using JacRed.Infrastructure.External;
using Xunit;

namespace JacRed.Tests.External;

public class AllohaTitleResolverNormalizeTests
{
    [Theory]
    [InlineData("tt0137523", "tt0137523", null)]
    [InlineData("TT0137523", "tt0137523", null)]
    [InlineData("tt1234567", "tt1234567", null)]
    [InlineData("kp301", "kp301", null)]
    [InlineData("KP301", "kp301", null)]
    [InlineData("kp464963", "kp464963", null)]
    [InlineData("tmdb1315772", "tmdb1315772", null)]
    [InlineData("tmdb:1315772", "tmdb1315772", null)]
    [InlineData("TMDB550", "tmdb550", null)]
    public void TryNormalizeId_CompactIds(string raw, string expectedId, string expectedHint)
    {
        Assert.True(AllohaTitleResolver.TryNormalizeId(raw, out string id, out string hint));
        Assert.Equal(expectedId, id);
        Assert.Equal(expectedHint, hint);
    }

    [Theory]
    [InlineData("https://www.themoviedb.org/movie/1315772-minions-monsters", "tmdb1315772", "movie")]
    [InlineData("https://www.themoviedb.org/tv/1399-game-of-thrones", "tmdb1399", "serial")]
    [InlineData("http://themoviedb.org/movie/550", "tmdb550", "movie")]
    [InlineData("https://www.themoviedb.org/movie/1315772-minions-monsters?language=en-US", "tmdb1315772", "movie")]
    public void TryNormalizeId_TmdbUrls(string raw, string expectedId, string expectedHint)
    {
        Assert.True(AllohaTitleResolver.TryNormalizeId(raw, out string id, out string hint));
        Assert.Equal(expectedId, id);
        Assert.Equal(expectedHint, hint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Fight Club")]
    [InlineData("1315772")]
    [InlineData("imdb0137523")]
    [InlineData("https://www.imdb.com/title/tt0137523/")]
    public void TryNormalizeId_RejectsNonIds(string raw)
    {
        Assert.False(AllohaTitleResolver.TryNormalizeId(raw, out _, out _));
        Assert.False(AllohaTitleResolver.IsResolvableId(raw));
    }

    [Fact]
    public void IsResolvableId_MatchesAlias()
    {
        Assert.True(AllohaTitleResolver.IsResolvableId("tmdb550"));
        Assert.True(AllohaTitleResolver.IsImdbOrKpId("kp301"));
        Assert.False(AllohaTitleResolver.IsImdbOrKpId("Matrix"));
    }
}
