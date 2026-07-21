using JacRed.Infrastructure.Tracks;
using Xunit;

namespace JacRed.Tests.Tracks;

public class TracksAnalyzerTheBadAndAttemptTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "sport" }, true)]
    [InlineData(new[] { "tvshow" }, true)]
    [InlineData(new[] { "docuserial" }, true)]
    [InlineData(new[] { "movie" }, false)]
    [InlineData(new[] { "serial" }, false)]
    [InlineData(new[] { "movie", "sport" }, true)]
    public void theBad_filters_unsupported_types(string[] types, bool expectedBad)
    {
        Assert.Equal(expectedBad, TracksAnalyzer.theBad(types));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(19, 20)]
    public void NextFailureAttempt_increments_without_exhaust_on_400(int current, int expected)
    {
        // Policy: single HTTP 400 must not jump to tracksatempt — only +1
        Assert.Equal(expected, TracksAnalyzer.NextFailureAttempt(current));
    }

    [Fact]
    public void GetInterItemDelayMs_uses_tracksdelay_with_jitter_bounds()
    {
        int baseMs = AppInit.conf.tracksdelay;
        if (baseMs <= 0)
        {
            Assert.Equal(0, TracksCron.GetInterItemDelayMs());
            return;
        }

        int jitter = System.Math.Max(1, baseMs / 10);
        for (int i = 0; i < 20; i++)
        {
            int delay = TracksCron.GetInterItemDelayMs();
            Assert.InRange(delay, baseMs - jitter, baseMs + jitter);
        }
    }
}
