using JacRed.Infrastructure.Indexers;
using Xunit;

namespace JacRed.Tests.Indexers;

public class TorznabXmlFormatterIndexersTests
{
    [Fact]
    public void IndexersXml_IncludesAllAndPerTracker_EscapesXml()
    {
        var xml = TorznabXmlFormatter.IndexersXml(new[] { "rutracker", "a&b" });

        Assert.Contains("id=\"all\"", xml);
        Assert.Contains("id=\"rutracker\"", xml);
        Assert.Contains("id=\"a&amp;b\"", xml);
        Assert.Contains("<title>a&amp;b</title>", xml);
        Assert.Contains("</indexers>", xml);
    }

    [Fact]
    public void IndexersXml_NullTrackers_OnlyAll()
    {
        var xml = TorznabXmlFormatter.IndexersXml(null);

        Assert.Contains("id=\"all\"", xml);
        Assert.DoesNotContain("JacRed tracker:", xml);
    }
}
