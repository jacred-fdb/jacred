using System;
using JacRed.Infrastructure.Trackers.Rutracker;
using JacRed.Models.Details;
using Xunit;

namespace JacRed.Tests.Rutracker;

public class RutrackerParserMagnetTests
{
    const string Title = "Укрытие / Бункер / Silo / Сезон: 2 / Серии: 1-10 из 10";
    const string Size = "101.97 GB";
    const string Magnet = "magnet:?xt=urn:btih:EE33E008E78DDE68559CDA46AA36C0A6B301DB58&tr=http://bt4.t-ru.org/ann?magnet";

    static TorrentDetails Cached(
        string title = Title,
        string size = Size,
        string magnet = Magnet,
        DateTime? create = null,
        DateTime? update = null) => new()
    {
        title = title,
        sizeName = size,
        magnet = magnet,
        createTime = create ?? new DateTime(2026, 2, 4, 12, 8, 3, DateTimeKind.Utc),
        updateTime = update ?? new DateTime(2026, 2, 4, 12, 8, 3, DateTimeKind.Utc)
    };

    static TorrentDetails Listing(
        string title = Title,
        string size = Size,
        DateTime? create = null) => new()
    {
        title = title,
        sizeName = size,
        createTime = create ?? new DateTime(2026, 2, 4, 1, 51, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void ShouldSkipTopicFetch_SameTitleSizeAndFreshMagnet_Skips()
    {
        Assert.True(RutrackerParser.ShouldSkipTopicFetch(Cached(), Listing()));
    }

    [Fact]
    public void ShouldSkipTopicFetch_TitleChanged_Fetches()
    {
        var listing = Listing(title: Title + " [4k]");
        Assert.False(RutrackerParser.ShouldSkipTopicFetch(Cached(), listing));
    }

    [Fact]
    public void ShouldSkipTopicFetch_SizeChanged_Fetches()
    {
        Assert.False(RutrackerParser.ShouldSkipTopicFetch(Cached(), Listing(size: "120 GB")));
    }

    [Fact]
    public void ShouldSkipTopicFetch_ListingNewerThanMagnetWrite_Fetches()
    {
        // Silo: listing createTime July, magnet updateTime February, title/size unchanged.
        var listing = Listing(create: new DateTime(2026, 7, 5, 1, 51, 0, DateTimeKind.Utc));
        Assert.False(RutrackerParser.ShouldSkipTopicFetch(Cached(), listing));
    }

    [Fact]
    public void ShouldSkipTopicFetch_EmptyMagnet_Fetches()
    {
        Assert.False(RutrackerParser.ShouldSkipTopicFetch(Cached(magnet: ""), Listing()));
        Assert.False(RutrackerParser.ShouldSkipTopicFetch(null, Listing()));
    }

    [Fact]
    public void ShouldSkipTopicFetch_NbspSize_StillEqual()
    {
        var listing = Listing(size: "101.97\u00a0GB");
        Assert.True(RutrackerParser.ShouldSkipTopicFetch(Cached(), listing));
    }

    [Fact]
    public void ApplyTopicPageDetails_HtmlDecodesMagnetAmpersands()
    {
        var t = new TorrentDetails();
        string html =
            "<a class=\"p-link small\" href=\"viewtopic.php?t=6601495\">05-07-26 01:51</a>" +
            "<a href=\"magnet:?xt=urn:btih:DD734DA14142D211010A82D540431A86C737498E&amp;tr=http%3A%2F%2Fbt4.t-ru.org%2Fann%3Fmagnet&amp;dn=Silo\" class=\"magnet-link\">";

        Assert.True(RutrackerParser.ApplyTopicPageDetails(t, html));
        Assert.Equal(
            "magnet:?xt=urn:btih:DD734DA14142D211010A82D540431A86C737498E&tr=http%3A%2F%2Fbt4.t-ru.org%2Fann%3Fmagnet&dn=Silo",
            t.magnet);
    }

    [Fact]
    public void ApplyTopicPageDetails_MedMagnetLinkClass_Matches()
    {
        var t = new TorrentDetails();
        string html = "<a href=\"magnet:?xt=urn:btih:AABB\" class=\"med magnet-link\">";
        Assert.True(RutrackerParser.ApplyTopicPageDetails(t, html));
        Assert.Equal("magnet:?xt=urn:btih:AABB", t.magnet);
    }
}
