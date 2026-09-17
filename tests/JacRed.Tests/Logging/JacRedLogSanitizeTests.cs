using JacRed.Infrastructure.Logging;
using Xunit;

namespace JacRed.Tests.Logging;

public class JacRedLogSanitizeTests
{
    [Fact]
    public void FormatLine_strips_cr_lf()
    {
        var line = JacRedLog.FormatLine("host", "ok\r\nfake: pwned\n");
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Contains("ok", line);
        Assert.Contains("fake: pwned", line);
    }
}
