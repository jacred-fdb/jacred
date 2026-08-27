using System;
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
}
