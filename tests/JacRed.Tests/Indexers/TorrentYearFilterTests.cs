using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Xunit;

namespace JacRed.Tests.Indexers;

/// <summary>The id search year by torrent: copies of one infohash judged together, whatever their magnet looks like.</summary>
public class TorrentYearFilterTests
{
    const string Hex = "0123456789abcdef0123456789abcdef01234567";

    static Result Copy(string magnet, int year) => new() { MagnetUri = magnet, Title = "t", info = new TorrentInfo { relased = year, types = new[] { "movie" } } };

    static List<Result>[] Filter(IReadOnlyList<List<Result>> batches, int year, out List<Result> otherYearCopies) =>
        IndexerResultFilters.FilterTorrentsByYear(batches, year, movieLike: true, out otherYearCopies);

    [Fact]
    public void Base32AndHexMagnetsOfOneTorrentAreJudgedTogether()
    {
        var dated = Copy("magnet:?xt=urn:btih:" + Base32(Convert.FromHexString(Hex)) + "&dn=x&tr=udp://a", 2023);
        var undated = Copy("magnet:?xt=urn:btih:" + Hex.ToUpperInvariant(), 0);

        var kept = Filter(new[] { new List<Result> { undated }, new List<Result> { dated } }, 1964, out var otherYear);

        Assert.Empty(kept[0]);
        Assert.Empty(kept[1]);
        Assert.Empty(otherYear);
    }

    [Fact]
    public void CopyOfAnotherYearOfAKeptTorrentMovesToOtherYearCopies()
    {
        var film = Copy("magnet:?xt=urn:btih:" + Hex, 2003);
        var remaster = Copy("magnet:?xt=urn:btih:" + Hex.ToUpperInvariant(), 2015);

        var kept = Filter(new[] { new List<Result> { remaster }, new List<Result> { film } }, 2003, out var otherYear);

        Assert.Empty(kept[0]);
        Assert.Same(film, Assert.Single(kept[1]));
        Assert.Same(remaster, Assert.Single(otherYear));
    }

    [Fact]
    public void MagnetsWithoutAnInfohashDoNotThrow()
    {
        var batch = new List<Result>
        {
            Copy(null, 0), Copy("", 2023), Copy("   ", 0), Copy("http://example.test/a.torrent", 2023), Copy("magnet:?xt=urn:btih:zzz", 0),
            Copy("magnet:?dn=only", 1964), Copy("magnet:?xt=urn:btih:" + Hex, 0), new Result { MagnetUri = "magnet:?xt=urn:btih:" + Hex }, new Result(),
            Copy("magnet:?xt=urn:btmh:1220" + Hex + Hex.Substring(0, 24), 0), null,
        };

        var kept = Filter(new[] { batch }, 1964, out var otherYear);

        // Only the copies dated otherwise go; with no infohash they are no copies of a kept torrent.
        Assert.Equal(batch.Count - 2, kept[0].Count);
        Assert.Empty(otherYear);
    }

    static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new System.Text.StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }
}
