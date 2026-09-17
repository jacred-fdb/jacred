using JacRed.Infrastructure.Networking;
using Xunit;

namespace JacRed.Tests.Networking;

public class CfFetchTests
{
    public CfFetchTests()
    {
        CfFetch.Reset();
        _ = AppInit.conf;
        AppInit.conf.cffetch.enable = true;
        AppInit.conf.cffetch.url = "http://127.0.0.1:8192/fetch";
    }

    [Fact]
    public void ClearanceLost_WhenCfMitigated()
    {
        Assert.True(CfFetch.ClearanceLost(403, "", cfMitigated: true));
        Assert.True(CfFetch.ClearanceLost(503, "", cfMitigated: true));
    }

    [Fact]
    public void NakedTracker403_IsNotClearanceLoss()
    {
        Assert.False(CfFetch.ClearanceLost(403, "<html>Тема находится в закрытом разделе</html>"));
        Assert.False(CfFetch.ClearanceLost(503, "<html>Форум временно недоступен</html>"));
    }

    [Fact]
    public void ChallengeHtml_Under200_IsClearanceLoss()
    {
        Assert.True(CfFetch.ClearanceLost(200, "<html><head><title>Just a moment...</title>"));
        Assert.True(CfFetch.ClearanceLost(200, "<script>window._cf_chl_opt={};</script>"));
    }

    [Fact]
    public void OrdinaryPageFailure_DoesNotDropCookie()
    {
        Assert.False(CfFetch.ClearanceLost(404, "<html>Тема не найдена</html>"));
        Assert.False(CfFetch.ClearanceLost(200, "<html><table class=\"tCenter\">раздачи</table></html>"));
    }

    [Fact]
    public void Remember_StoresPerHost()
    {
        const string host = "fast-remember.test";
        CfFetch.Forget(host);

        Assert.Null(CfFetch.For(host));

        CfFetch.Remember(host, "cf_clearance=abc; bb_session=xyz", "Mozilla/5.0 Chrome/148");

        var got = CfFetch.For(host);
        Assert.NotNull(got);
        Assert.Equal("cf_clearance=abc; bb_session=xyz", got.Cookies);
        Assert.Equal("Mozilla/5.0 Chrome/148", got.UserAgent);
    }

    [Fact]
    public void ForUncleared_ReturnsClearanceWithoutCookie()
    {
        const string host = "uncleared.test";
        CfFetch.Forget(host);

        // обычный путь без cookie ничего не даёт...
        Assert.Null(CfFetch.For(host));

        // ...но blind-путь (только TLS-impersonate) доступен
        var got = CfFetch.ForUncleared(host);
        Assert.NotNull(got);
        Assert.Null(got.Cookies);
    }

    [Fact]
    public void ForUncleared_NullWhenCffetchDisabled()
    {
        const string host = "uncleared-disabled.test";
        bool previous = AppInit.conf.cffetch.enable;
        try
        {
            AppInit.conf.cffetch.enable = false;
            Assert.Null(CfFetch.ForUncleared(host));
        }
        finally
        {
            AppInit.conf.cffetch.enable = previous;
        }
    }

    [Fact]
    public void ForUncleared_NullWhenFastPathBlocked()
    {
        const string host = "uncleared-blocked.test";
        CfFetch.BlockFastPath(host);
        try
        {
            Assert.Null(CfFetch.ForUncleared(host));
        }
        finally
        {
            CfFetch.Reset();
        }
    }

    [Fact]
    public void Forget_RemovesHost()
    {
        const string host = "fast-forget.test";

        CfFetch.Remember(host, "cf_clearance=abc", "Mozilla/5.0");
        Assert.NotNull(CfFetch.For(host));

        CfFetch.Forget(host);
        Assert.Null(CfFetch.For(host));
    }

    [Fact]
    public void EmptyCookie_IsNotRemembered()
    {
        const string host = "fast-empty.test";
        CfFetch.Forget(host);

        CfFetch.Remember(host, "", "Mozilla/5.0");
        CfFetch.Remember(host, null, "Mozilla/5.0");

        Assert.Null(CfFetch.For(host));
    }

    [Fact]
    public void Remember_MergesJar_DoesNotReplace()
    {
        const string host = "fast-merge.test";
        CfFetch.Forget(host);

        CfFetch.Remember(host, "cf_clearance=abc; bb_session=old", "Mozilla/5.0 Chrome/148");
        CfFetch.Remember(host, "bb_session=new", null);

        var got = CfFetch.For(host);
        Assert.Contains("cf_clearance=abc", got.Cookies);
        Assert.Contains("bb_session=new", got.Cookies);
        Assert.DoesNotContain("bb_session=old", got.Cookies);
        Assert.Equal("Mozilla/5.0 Chrome/148", got.UserAgent);
    }

    [Fact]
    public void ShouldDropClearance_AfterThreeInWindow()
    {
        const string host = "fast-mitigation.test";
        CfFetch.Reset();

        Assert.False(CfFetch.ShouldDropClearance(host));
        Assert.False(CfFetch.ShouldDropClearance(host));
        Assert.True(CfFetch.ShouldDropClearance(host));
        Assert.False(CfFetch.ShouldDropClearance(host));
    }

    [Fact]
    public void Hosts_DoNotShareJar()
    {
        CfFetch.Remember("fast-a.test", "cf_clearance=a", "UA-A");
        CfFetch.Remember("fast-b.test", "cf_clearance=b", "UA-B");

        Assert.Equal("cf_clearance=a", CfFetch.For("fast-a.test").Cookies);
        Assert.Equal("cf_clearance=b", CfFetch.For("fast-b.test").Cookies);

        CfFetch.Forget("fast-a.test");

        Assert.Null(CfFetch.For("fast-a.test"));
        Assert.NotNull(CfFetch.For("fast-b.test"));
    }
}
