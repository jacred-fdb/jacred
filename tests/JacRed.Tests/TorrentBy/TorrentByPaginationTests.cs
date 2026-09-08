using System.Text.RegularExpressions;
using JacRed.Infrastructure.Trackers.TorrentBy;
using Xunit;

namespace JacRed.Tests.TorrentBy;

public class TorrentByPaginationTests
{
    [Fact]
    public void LegacyMaxPageRegex_DoesNotMatch_FilmsFixture()
    {
        string html = FixtureLoader.Read("TorrentBy/browse_films.html");
        Assert.False(Regex.Match(html, TorrentByPagination.LegacyMaxPageRegex).Success);
    }

    [Fact]
    public void ParsePager_FilmsFixture_HasTrailingEllipsisJump10()
    {
        string html = FixtureLoader.Read("TorrentBy/browse_films.html");
        var pager = TorrentByPagination.ParsePager(html);

        Assert.Equal(1, pager.CurrentDisplay);
        Assert.Equal(10, pager.MaxPageIndex);
        Assert.True(pager.HasTrailingEllipsis);
        Assert.Equal(10, pager.EllipsisJumpPage);
    }

    [Theory]
    [InlineData("browse_anime.html", 1)]
    [InlineData("browse_tv.html", 1)]
    public void ParsePager_AnimeAndTvFixtures_EllipsisJumpIs1(string fixture, int jump)
    {
        string html = FixtureLoader.Read($"TorrentBy/{fixture}");
        var pager = TorrentByPagination.ParsePager(html);

        Assert.True(pager.HasTrailingEllipsis);
        Assert.Equal(jump, pager.EllipsisJumpPage);
        Assert.Equal(jump, pager.MaxPageIndex);
    }

    [Fact]
    public void ParsePager_LastPageWithoutEllipsis_UsesCurrentDisplay()
    {
        const string html = """
            <center style="margin-top:5px;">Страницы:
            <a href="?page=47"><span class="circle_page">48</span></a>
            <a href="?page=48"><span class="circle_page">49</span></a>
            <span class="circle_page" style="background:#BFDBFF;">50</span>
            </center></td>
            """;

        var pager = TorrentByPagination.ParsePager(html);

        Assert.Equal(50, pager.CurrentDisplay);
        Assert.Equal(49, pager.MaxPageIndex);
        Assert.False(pager.HasTrailingEllipsis);
        Assert.Null(pager.EllipsisJumpPage);
    }

    [Fact]
    public void ParsePager_MissingPager_ReturnsPageZero()
    {
        var pager = TorrentByPagination.ParsePager("<table><tr class=\"ttable_col1\"></tr></table>");
        Assert.Equal(0, pager.MaxPageIndex);
        Assert.False(pager.HasTrailingEllipsis);
        Assert.Null(pager.EllipsisJumpPage);
    }
}
