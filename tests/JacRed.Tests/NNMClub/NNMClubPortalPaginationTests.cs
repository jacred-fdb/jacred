using System.Collections.Generic;
using JacRed.Infrastructure.Trackers.NNMClub;
using JacRed.Models.tParse;
using Xunit;

namespace JacRed.Tests.NNMClub;

public class NNMClubPortalPaginationTests
{
    static string Fixture(string name) => FixtureLoader.Read($"NNMClub/{name}");

    [Theory]
    [InlineData(2333, 500)]
    [InlineData(499, 500)]
    [InlineData(500, 500)]
    [InlineData(84, 85)]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    public void ClampTaskPageCount_RespectsPortalLimit(int maxpages, int expected)
    {
        Assert.Equal(expected, NNMClubPortalPagination.ClampTaskPageCount(maxpages));
    }

    [Fact]
    public void IsPortalLimitFaq_DetectsFixture()
    {
        string html = Fixture("portal_limit_faq.html");
        Assert.True(NNMClubPortalPagination.IsPortalLimitFaq(html));
        Assert.Equal(
            NNMClubPortalPagination.PageParseStatus.PortalLimitFaq,
            NNMClubPortalPagination.ClassifyPage(html, torrentCount: 0));
    }

    [Fact]
    public void IsPortalLimitFaq_IgnoresNormalPortalFixture()
    {
        string html = Fixture("portal_c6.html");
        Assert.False(NNMClubPortalPagination.IsPortalLimitFaq(html));
        Assert.True(NNMClubPortalPagination.LooksLikePortalListing(html));
    }

    [Fact]
    public void ClassifyPage_EmptyPortal_Settles()
    {
        const string html = "<title>Cat :: NNM-Club</title><div class=\"paginport nav\">pages</div>";
        var status = NNMClubPortalPagination.ClassifyPage(html, torrentCount: 0);
        Assert.Equal(NNMClubPortalPagination.PageParseStatus.EmptyPortal, status);
        Assert.True(NNMClubPortalPagination.ShouldSettleTask(status));
    }

    [Fact]
    public void ClassifyPage_MissingTitle_IsTransient()
    {
        var status = NNMClubPortalPagination.ClassifyPage("<html></html>", torrentCount: 0);
        Assert.Equal(NNMClubPortalPagination.PageParseStatus.TransientError, status);
        Assert.False(NNMClubPortalPagination.ShouldSettleTask(status));
    }

    [Fact]
    public void ClassifyPage_WithTorrents_Ok()
    {
        const string html = "<title>Cat :: NNM-Club</title><div class=\"paginport nav\">pages</div>";
        var status = NNMClubPortalPagination.ClassifyPage(html, torrentCount: 3);
        Assert.Equal(NNMClubPortalPagination.PageParseStatus.OkWithTorrents, status);
        Assert.True(NNMClubPortalPagination.ShouldSettleTask(status));
    }

    [Fact]
    public void PruneTasksBeyondPortalLimit_RemovesHighPages()
    {
        var tasks = new List<TaskParse>
        {
            new(0),
            new(499),
            new(500),
            new(11371)
        };

        int pruned = NNMClubPortalPagination.PruneTasksBeyondPortalLimit(tasks);

        Assert.Equal(2, pruned);
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.True(t.page < NNMClubPortalPagination.MaxPortalPages));
    }

    [Fact]
    public void DiagnosticSmoke_PruneDropsTasksBeyondFirst500()
    {
        // From production diagnosis: 11372 total, 6286 beyond first 500 pages per-category aggregate.
        const int total = 11372;
        const int within500 = 5086;
        const int beyond500 = 6286;
        Assert.Equal(total, within500 + beyond500);

        Assert.Equal(NNMClubPortalPagination.MaxPortalPages, NNMClubPortalPagination.ClampTaskPageCount(2333));

        // Flat list of pages 0..11371 simulates an uncapped map; prune keeps 0..499.
        var all = new List<TaskParse>();
        for (int page = 0; page < total; page++)
            all.Add(new TaskParse(page));

        int pruned = NNMClubPortalPagination.PruneTasksBeyondPortalLimit(all);
        Assert.Equal(total - NNMClubPortalPagination.MaxPortalPages, pruned);
        Assert.Equal(NNMClubPortalPagination.MaxPortalPages, all.Count);
        Assert.True(pruned >= beyond500);
    }
}
