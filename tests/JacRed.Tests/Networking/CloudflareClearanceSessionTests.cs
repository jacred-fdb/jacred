using JacRed.Infrastructure.Networking;
using System.Collections.Generic;
using Xunit;

namespace JacRed.Tests.Networking;

public class CloudflareClearanceSessionTests
{
    [Fact]
    public void SessionNameFor_SanitizesHost_PerTracker()
    {
        Assert.Equal("jacred-kinozal_guru", CloudflareClearance.SessionNameFor("kinozal.guru"));
        Assert.Equal("jacred-rutracker_org", CloudflareClearance.SessionNameFor("rutracker.org"));
        Assert.Equal("jacred-anibelka_com", CloudflareClearance.SessionNameFor("anibelka.com"));
        Assert.NotEqual(
            CloudflareClearance.SessionNameFor("kinozal.guru"),
            CloudflareClearance.SessionNameFor("rutracker.org"));
    }

    [Fact]
    public void SessionNameFor_Empty_IsBarePrefix()
    {
        Assert.Equal("jacred", CloudflareClearance.SessionNameFor(null));
        Assert.Equal("jacred", CloudflareClearance.SessionNameFor(""));
        Assert.Equal("jacred", CloudflareClearance.SessionNameFor("   "));
    }

    [Fact]
    public void SessionNameFor_IsCaseInsensitive_AndCharsetSafe()
    {
        Assert.Equal(
            CloudflareClearance.SessionNameFor("Kinozal.GURU"),
            CloudflareClearance.SessionNameFor("kinozal.guru"));
        string name = CloudflareClearance.SessionNameFor("v30.astar.bz");
        Assert.StartsWith("jacred-", name);
        Assert.Matches("^[A-Za-z0-9_-]+$", name);
    }

    [Fact]
    public void ExtraBrowserHeaders_ForwardsReferer_SkipsCookieAndSecFetch()
    {
        var extra = new List<(string name, string val)>
        {
            ("Accept", "text/html"),
            ("Accept-Language", "ru"),
            ("Cookie", "secret=1"),
            ("User-Agent", "ignored"),
            ("Sec-Fetch-Site", "cross-site"),
            ("Referer", "https://www.yandex.ru/"),
        };

        var headers = CloudflareClearance.ExtraBrowserHeaders("https://www.google.com/", extra);

        Assert.Equal("https://www.yandex.ru/", headers["Referer"]);
        Assert.Equal("text/html", headers["Accept"]);
        Assert.Equal("ru", headers["Accept-Language"]);
        Assert.False(headers.ContainsKey("Cookie"));
        Assert.False(headers.ContainsKey("User-Agent"));
        Assert.False(headers.ContainsKey("Sec-Fetch-Site"));
    }

    [Fact]
    public void ExtraBrowserHeaders_RefererOnly_WhenNoExtra()
    {
        var headers = CloudflareClearance.ExtraBrowserHeaders("https://www.google.com/", null);
        Assert.Single(headers);
        Assert.Equal("https://www.google.com/", headers["Referer"]);
        Assert.Empty(CloudflareClearance.ExtraBrowserHeaders(null, null));
    }

    [Fact]
    public void Origin503_WithSearchReferer_FallsThroughToBrowser()
    {
        Assert.True(CloudflareClearance.ShouldSkipFastPathForOrigin503(
            503, "<html>503 Service Temporarily Unavailable</html>", "https://www.google.com/"));
        Assert.True(CloudflareClearance.ShouldSkipFastPathForOrigin503(503, "", "https://www.google.com/"));
        Assert.False(CloudflareClearance.ShouldSkipFastPathForOrigin503(503, "<html>503</html>", null));
        Assert.False(CloudflareClearance.ShouldSkipFastPathForOrigin503(404, "503 Service Temporarily Unavailable", "https://www.google.com/"));
        Assert.False(CloudflareClearance.ShouldSkipFastPathForOrigin503(403, "forbidden", "https://www.google.com/"));
    }
}
