using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.AnimeLayer;
using Xunit;

namespace JacRed.Tests.AnimeLayer;

public class AnimeLayerParserUnitTests
{
    const string Host = "https://animelayer.ru";

    [Fact]
    public void ParseTorrentListFromHtml_ExtractsListingSize()
    {
        const string html = """
            <div class="torrent-item torrent-item-medium panel">
                <h3 class="h2 m0">
                    <a href="/torrent/6a4a7093aa0a9cff9d0ddb83/">Kimi no Koto ga Dai Dai Dai Dai Daisuki na Hyakunin no Kanojo (2026) / Сто девушек [ТВ-3] (1-6)</a>
                </h3>
                <div class="info pd20">
                    <i class="icon s-icons-upload"></i>&nbsp;1
                    <span class="gray">&nbsp;|&nbsp;</span>
                    <i class="icon s-icons-download"></i>&nbsp;2
                    <span class="gray">&nbsp;|&nbsp;</span>
                    2.19 GB
                    <span class="gray">&nbsp;|&nbsp;</span>
                    <span class="gray">Обновлён:</span>&nbsp;9 августа в&nbsp;18:05
                </div>
                <strong>Год выхода: </strong>2026
            </div>
            """;

        var torrents = AnimeLayerParser.ParseTorrentListFromHtml(html, Host, page: 1);

        var torrent = Assert.Single(torrents);
        Assert.Equal($"{Host}/torrent/6a4a7093aa0a9cff9d0ddb83/", torrent.url);
        Assert.Equal("2.19 GB", torrent.sizeName);
        Assert.Equal(1, torrent.sid);
        Assert.Equal(2, torrent.pir);
        Assert.NotEqual(default(DateTime), torrent.createTime);
    }

    static string Row(string extra) => $"""
        <li class="torrent-item torrent-item-medium panel">
            <h3 class="h2 m0"><a href="/torrent/6abeaa7157d34b3b800e1662/">Koori no Jouheki 2nd Season / Ледяная стена 2 (1—8)</a></h3>
            <div class="info pd20">
                <i class="icon s-icons-upload"></i>&nbsp;4		<span class="gray">&nbsp;|&nbsp;</span>
                <i class="icon s-icons-download"></i>&nbsp;1		<span class="gray">&nbsp;|&nbsp;</span>
                1.96 GB
                <span class="gray">Обновлён:</span>&nbsp;19 ноября в&nbsp;21:09
            </div>
            <div class="description">
                <strong>Год выхода: </strong>2026<br />
                <u>Дополнительно</u><br />
                <strong>Кодек: </strong>AVC (h.264)<br />
                <strong>Разрешение: </strong>1280x720<br />
        {extra}
            </div>
        </li>
        """;

    [Fact]
    public void ParseTorrentListFromHtml_StatedSubtitlesAndAudio()
    {
        // As the listing has them: tabs after the value, before <br />.
        var torrent = Assert.Single(AnimeLayerParser.ParseTorrentListFromHtml(Row(
            "<strong>Субтитры: </strong>русские (софтсаб), английские (полухардсаб)<br />\n<strong>Язык: </strong>японский\t\t\t<br /><br />"),
            Host, page: 1));

        Assert.Equal(new[] { "eng", "rus" }, torrent.subtitleLanguages.OrderBy(c => c));
        Assert.Equal(new[] { "jpn" }, torrent.audioLanguages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<strong>Субтитры: </strong>нет<br />")]
    [InlineData("<strong>Субтитры: </strong>1. Сrunchyroll 2. SovetRomantica<br /><strong>Язык: </strong><br />")]
    public void ParseTorrentListFromHtml_NoKnownLanguage_FieldsNull(string extra)
    {
        var torrent = Assert.Single(AnimeLayerParser.ParseTorrentListFromHtml(Row(extra), Host, page: 1));

        Assert.Null(torrent.subtitleLanguages);
        Assert.Null(torrent.audioLanguages);
    }
}
