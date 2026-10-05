using JacRed.Infrastructure.External;
using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace JacRed.Tests.Indexers;

/// <summary>
/// With evercache, FileDB.OpenRead hands search the live cached records. Two duplicates
/// (same magnet) are merged by the search; the cached records must stay as they were,
/// otherwise concurrent searches over the same key race on the same HashSet (#245).
/// </summary>
public class SearchCacheIsolationTests : IDisposable
{
    const string RuName = "Сёгун кэш тест";
    const string EnName = "Shogun Cache Test";
    const string ImdbId = "tt9900245";
    const string Magnet = "magnet:?xt=urn:btih:fedcba9876543210fedcba9876543210fedcba98";

    readonly string _bucketKey;
    readonly string _fdbPath;

    public SearchCacheIsolationTests()
    {
        _bucketKey = FileDB.KeyForTorrent(RuName, EnName);
        _fdbPath = FileDB.PathForKey(_bucketKey);

        using var fdb = FileDB.OpenWrite(_bucketKey);
        // Voices come from the tracker name: lostfilm → LostFilm, hdrezka → HDRezka.
        fdb.AddOrUpdate(Torrent("lostfilm"));
        fdb.AddOrUpdate(Torrent("hdrezka"));
    }

    public void Dispose()
    {
        try
        {
            using (var fdb = FileDB.OpenWrite(_bucketKey))
            {
                fdb.Database.Clear();
                fdb.savechanges = true;
            }
        }
        catch
        {
            // ignore cleanup races
        }

        FileDB.RemoveKeyFromMasterDb(_bucketKey);

        try
        {
            if (File.Exists(_fdbPath))
                File.Delete(_fdbPath);
        }
        catch
        {
            // ignore
        }
    }

    static TorrentDetails Torrent(string tracker) => new()
    {
        url = $"https://example.test/search-cache-isolation/{tracker}",
        trackerName = tracker,
        types = new[] { "serial" },
        title = $"{RuName} / {EnName} (2024)",
        name = RuName,
        originalname = EnName,
        magnet = Magnet,
        sid = 10,
        pir = 1,
        sizeName = "1 GB",
        relased = 2024,
        createTime = DateTime.UtcNow,
        updateTime = DateTime.UtcNow
    };

    [Fact]
    public async Task CombinedSearch_MergingDuplicates_DoesNotMutateCachedRecords()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"alloha:title:{ImdbId}", new AllohaResolveResult { Search = EnName, AltName = RuName, ImdbId = ImdbId }, TimeSpan.FromHours(1));

        for (int i = 0; i < 2; i++)
        {
            var results = await IndexerSearchEngine.SearchCombinedAsync(
                new IndexerSearchRequest { Query = ImdbId, CardMode = false },
                cache,
                jackettSearch: null);

            var merged = Assert.Single(results);
            Assert.Equal(new[] { "HDRezka", "LostFilm" }, merged.info.voices.OrderBy(v => v));
        }

        var records = FileDB.OpenRead(_bucketKey).Values;
        Assert.Equal(new[] { "LostFilm" }, records.Single(t => t.trackerName == "lostfilm").voices);
        Assert.Equal(new[] { "HDRezka" }, records.Single(t => t.trackerName == "hdrezka").voices);
    }
}
