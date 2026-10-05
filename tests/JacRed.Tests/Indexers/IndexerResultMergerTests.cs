using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Xunit;

namespace JacRed.Tests.Indexers;

public class IndexerResultMergerTests
{
    const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

    static Result Item(int seeders, params string[] voices) => new()
    {
        MagnetUri = Magnet,
        Seeders = seeders,
        info = new TorrentInfo { voices = new HashSet<string>(voices) },
    };

    [Fact]
    public void MergeAndSort_DoesNotMutateInputs()
    {
        // Batches may be result lists shared across requests (IMemoryCache with evercache.validHour: 0).
        var first = Item(1, "LostFilm");
        var second = Item(5, "HDRezka");

        var merged = Assert.Single(IndexerResultMerger.MergeAndSort(new[] { first }, new[] { second }));

        Assert.Equal(new[] { "HDRezka", "LostFilm" }, merged.info.voices.OrderBy(v => v));
        Assert.Equal(5, merged.Seeders);
        Assert.Equal(new[] { "LostFilm" }, first.info.voices);
        Assert.Equal(1, first.Seeders);
        Assert.Equal(new[] { "HDRezka" }, second.info.voices);
    }
}
