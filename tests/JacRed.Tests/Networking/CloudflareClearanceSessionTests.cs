using JacRed.Infrastructure.Networking;
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
}
