using System;
using System.IO;
using JacRed.Infrastructure.Parsing;
using Xunit;

namespace JacRed.Tests.Parsing;

public class BencodeToMagnetNoTrackersTests
{
    static byte[] SampleTorrent()
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "sample_passkey.torrent"));

    [Fact]
    public void Magnet_IncludesAnnounceWithPasskey()
    {
        string magnet = BencodeTo.Magnet(SampleTorrent());
        Assert.NotNull(magnet);
        Assert.Contains("tr=", magnet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SECRET_PASSKEY_XYZ", magnet, StringComparison.Ordinal);
    }

    [Fact]
    public void MagnetNoTrackers_HasXtAndDn_WithoutTrOrPasskey()
    {
        string magnet = BencodeTo.MagnetNoTrackers(SampleTorrent());
        Assert.NotNull(magnet);
        Assert.StartsWith("magnet:?xt=urn:btih:", magnet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tr=", magnet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_PASSKEY_XYZ", magnet, StringComparison.Ordinal);
        Assert.DoesNotContain("announce", magnet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MagnetNoTrackers_NullInput_ReturnsNull()
    {
        Assert.Null(BencodeTo.MagnetNoTrackers(null));
    }
}
