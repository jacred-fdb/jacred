using JacRed.Application.Search;
using JacRed.Infrastructure.External;
using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace JacRed.Tests.External;

/// <summary>
/// Seeds an isolated FileDB bucket + Alloha memory-cache entries (no live HTTP),
/// then searches via native API and combined indexer path using tt/kp/tmdb ids.
/// </summary>
public class AllohaFileDbSearchTests : IDisposable
{
    const string RuName = "Бойцовский клуб";
    const string EnName = "Fight Club";
    const string ImdbId = "tt0137523";
    const string KpId = "kp361";
    const string TmdbId = "tmdb550";
    const string TestUrl = "https://example.test/alloha-fdb-search/fight-club";
    const string TestMagnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

    readonly string _bucketKey;
    readonly string _tracker;
    readonly string _fdbPath;
    readonly AllohaResolveResult _resolved;

    public AllohaFileDbSearchTests()
    {
        _tracker = PickTracker();
        _bucketKey = FileDB.KeyForTorrent(RuName, EnName);
        _fdbPath = FileDB.PathForKey(_bucketKey);

        _resolved = new AllohaResolveResult
        {
            Search = EnName,
            AltName = RuName,
            Year = 1999,
            Type = "movie",
            ImdbId = ImdbId,
            KpId = KpId,
            TmdbId = TmdbId
        };

        SeedFileDb();
    }

    public void Dispose()
    {
        try
        {
            // Drop write connection so Dispose can remove openWriteTask entry.
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

    [Theory]
    [InlineData(ImdbId)]
    [InlineData(KpId)]
    [InlineData(TmdbId)]
    public async Task NativeQuery_IdResolve_FindsSeededTorrent(string id)
    {
        using var cache = NewCacheWithResolve(id);
        var svc = new TorrentQueryService();

        var raw = await svc.QueryTorrentsAsync(
            search: id,
            altname: null,
            exact: false,
            type: null,
            sort: "sid",
            tracker: null,
            voice: null,
            videotype: null,
            relased: 0,
            quality: 0,
            season: 0,
            memoryCache: cache);

        var rows = ToRows(raw);
        Assert.Contains(rows, r => string.Equals((string)r["url"], TestUrl, StringComparison.OrdinalIgnoreCase)
            || string.Equals((string)r["magnet"], TestMagnet, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r => (string)r["originalname"] == EnName || (string)r["name"] == RuName);
    }

    [Theory]
    [InlineData(ImdbId)]
    [InlineData(KpId)]
    [InlineData("TT0137523")]
    [InlineData("KP361")]
    public async Task CombinedSearch_IdResolve_FindsSeededTorrent(string id)
    {
        using var cache = NewCacheWithResolve(id);
        var results = await IndexerSearchEngine.SearchCombinedAsync(
            new IndexerSearchRequest { Query = id, CardMode = false },
            cache,
            jackettSearch: null);

        Assert.NotEmpty(results);
        Assert.Contains(results, r =>
            string.Equals(r.MagnetUri, TestMagnet, StringComparison.OrdinalIgnoreCase)
            || (r.info != null && (r.info.originalname == EnName || r.info.name == RuName)));
    }

    [Fact]
    public async Task NativeQuery_WrongYearFromAlloha_FiltersOut()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var wrongYear = new AllohaResolveResult
        {
            Search = EnName,
            AltName = RuName,
            Year = 2010,
            Type = "movie",
            ImdbId = ImdbId,
            KpId = KpId,
            TmdbId = TmdbId
        };
        SeedCache(cache, ImdbId, wrongYear);

        var prev = AppInit.conf.alloha.filterByYear;
        AppInit.conf.alloha.filterByYear = true;
        try
        {
            var svc = new TorrentQueryService();
            var raw = await svc.QueryTorrentsAsync(
                ImdbId, null, false, null, "sid", null, null, null, 0, 0, 0, cache);
            Assert.Empty(ToRows(raw));
        }
        finally
        {
            AppInit.conf.alloha.filterByYear = prev;
        }
    }

    MemoryCache NewCacheWithResolve(string id)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.True(AllohaTitleResolver.TryNormalizeId(id, out string canonical, out _));
        SeedCache(cache, canonical, _resolved);
        // Cross-id keys so ResolveAsync cache aliases behave like production.
        SeedCache(cache, ImdbId, _resolved);
        SeedCache(cache, KpId, _resolved);
        SeedCache(cache, TmdbId, _resolved);
        return cache;
    }

    static void SeedCache(IMemoryCache cache, string id, AllohaResolveResult resolved)
    {
        cache.Set($"alloha:title:{id.Trim().ToLowerInvariant()}", resolved, TimeSpan.FromHours(1));
    }

    void SeedFileDb()
    {
        using var fdb = FileDB.OpenWrite(_bucketKey);
        fdb.AddOrUpdate(new TorrentDetails
        {
            url = TestUrl,
            trackerName = _tracker,
            types = new[] { "movie" },
            title = $"{RuName} / {EnName} (1999)",
            name = RuName,
            originalname = EnName,
            magnet = TestMagnet,
            sid = 10,
            pir = 1,
            sizeName = "1 GB",
            relased = 1999,
            createTime = DateTime.UtcNow,
            updateTime = DateTime.UtcNow
        });
        Assert.True(FileDB.masterDb.ContainsKey(_bucketKey));
    }

    static string PickTracker()
    {
        var sync = AppInit.conf.synctrackers;
        if (sync != null && sync.Length > 0)
            return sync[0];

        var disabled = AppInit.conf.disable_trackers;
        string candidate = "rutor";
        if (disabled != null && disabled.Contains(candidate))
            candidate = "kinozal";
        return candidate;
    }

    static List<JObject> ToRows(object raw)
    {
        if (raw == null)
            return new List<JObject>();

        if (raw is JArray ja)
            return ja.Children<JObject>().ToList();

        string json = JsonConvert.SerializeObject(raw);
        var token = JToken.Parse(json);
        if (token is JArray arr)
            return arr.Children<JObject>().ToList();

        return new List<JObject>();
    }
}
