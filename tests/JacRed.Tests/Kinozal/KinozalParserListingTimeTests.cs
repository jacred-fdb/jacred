using System;
using JacRed.Infrastructure.Trackers.Kinozal;
using Xunit;

namespace JacRed.Tests.Kinozal;

public class KinozalParserListingTimeTests
{
    [Theory]
    [InlineData("16.07.2024 в 12:34", 2024, 7, 16, 12, 34)]
    [InlineData("01.01.2020 в 00:00", 2020, 1, 1, 0, 0)]
    public void ParseListingUpdateTime_Absolute(string raw, int y, int m, int d, int h, int min)
    {
        var dt = KinozalParser.ParseListingUpdateTime(raw);
        Assert.Equal(new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc), dt);
    }

    [Theory]
    [InlineData("сегодня в 09:15")]
    [InlineData("вчера в 23:59")]
    public void ParseListingUpdateTime_Relative_ReturnsNonDefault(string raw)
    {
        Assert.NotEqual(default, KinozalParser.ParseListingUpdateTime(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    public void ParseListingUpdateTime_Invalid_ReturnsDefault(string raw)
    {
        Assert.Equal(default, KinozalParser.ParseListingUpdateTime(raw));
    }
}
