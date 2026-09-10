using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Trackers.TorrentBy;
using JacRed.Models.tParse;
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
    [InlineData("browse_anime.html")]
    [InlineData("browse_tv.html")]
    public void ParsePager_AnimeAndTvFixtures_MissingPager_ReturnsPageZero(string fixture)
    {
        string html = FixtureLoader.Read($"TorrentBy/{fixture}");
        var pager = TorrentByPagination.ParsePager(html);

        Assert.Equal(0, pager.MaxPageIndex);
        Assert.False(pager.HasTrailingEllipsis);
        Assert.Null(pager.EllipsisJumpPage);
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

    [Fact]
    public void PrunePagesBeyondMax_DropsGhostTail()
    {
        var tasks = Enumerable.Range(0, 20).Select(i => new TaskParse(i)).ToList();
        Assert.Equal(8, TorrentByPagination.PrunePagesBeyondMax(tasks, 11));
        Assert.Equal(12, tasks.Count);
        Assert.Equal(11, tasks[^1].page);
        Assert.Equal(0, TorrentByPagination.PrunePagesBeyondMax(tasks, 11));
        Assert.Equal(0, TorrentByPagination.PrunePagesBeyondMax(null, 5));
    }

    [Fact]
    public void PrunePagesBeyondMax_LastZeroKeepsOnlyPageZero()
    {
        var tasks = Enumerable.Range(0, 5).Select(i => new TaskParse(i)).ToList();
        Assert.Equal(4, TorrentByPagination.PrunePagesBeyondMax(tasks, 0));
        Assert.Equal(new[] { 0 }, tasks.Select(t => t.page).ToArray());
    }
}
