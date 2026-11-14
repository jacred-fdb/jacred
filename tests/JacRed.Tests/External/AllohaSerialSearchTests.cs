using JacRed.Application.Index;
using JacRed.Infrastructure.External;
using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace JacRed.Tests.External;

/// <summary>
/// Id search of a series: later seasons are released after the Alloha year,
/// and some records carry no parsed seasons.
/// </summary>
public class AllohaSerialSearchTests : IDisposable
{
    const string ImdbId = "tt3581920";
    const string UrlBase = "https://example.test/alloha-serial-search/";

    readonly string _ru;
    readonly string _en;
    readonly string _bucketKey;

    public AllohaSerialSearchTests()
    {
        string tag = Guid.NewGuid().ToString("N")[..12];
        _ru = "Одни из нас " + tag;
        _en = "The Last of Us " + tag;
        _bucketKey = FileDB.KeyForTorrent(_ru, _en);

        using (var fdb = FileDB.OpenWrite(_bucketKey))
        {
            fdb.AddOrUpdate(Torrent("s1", 2023, new[] { 1 }, $"{_ru} / {_en} [S01] (2023)"));
            fdb.AddOrUpdate(Torrent("s2", 2025, new[] { 2 }, $"{_ru} / {_en} [S02] (2025)"));
            fdb.AddOrUpdate(Torrent("s2-title", 2025, Array.Empty<int>(), $"{_ru} / {_en} S02 (2025)"));
            fdb.AddOrUpdate(Torrent("unknown", 2025, Array.Empty<int>(), $"{_ru} / {_en} (2025)"));
        }
        FastDbIndex.Default.RebuildIfChanged();
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
            string path = FileDB.PathForKey(_bucketKey);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public async Task SerialYear_KeepsLaterSeasons()
    {
        var results = await Search(serial: true, season: null);
        Assert.Equal(new[] { "s1", "s2", "s2-title", "unknown" }, Ids(results));
    }

    [Fact]
    public async Task MovieYear_StaysPlusMinusOne()
    {
        var results = await Search(serial: false, season: null);
        Assert.Equal(new[] { "s1" }, Ids(results));
    }

    [Fact]
    public async Task Season_MatchesTitleWhenSeasonsAreMissing()
    {
        var results = await Search(serial: true, season: 2);
        Assert.Equal(new[] { "s2", "s2-title" }, Ids(results));
    }

    [Theory]
    [InlineData("serial", null, "serial", true)]
    [InlineData("tv-show", null, "serial", true)]
    [InlineData("anime-serial", null, "anime", true)]
    [InlineData("anime", null, "anime", false)]
    [InlineData("movie", null, "movie", false)]
    [InlineData(null, "serial", "serial", true)]
    [InlineData(null, "movie", "movie", false)]
    public void MapTitles_IsSeriesFromCategory(string slug, string urlHint, string type, bool isSeries)
    {
        var r = AllohaTitleResolver.MapTitles("Original", "Name", null, 2020, slug, urlHint, null, null, null, null, null);
        Assert.Equal(type, r.Type);
        Assert.Equal(isSeries, r.IsSeries);
    }

    async Task<List<JacRed.Models.Api.Result>> Search(bool serial, int? season)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"alloha:title:{ImdbId}", new AllohaResolveResult
        {
            Search = _en,
            AltName = _ru,
            Year = 2023,
            Type = "serial",
            IsSeries = serial,
            ImdbId = ImdbId
        }, TimeSpan.FromHours(1));

        var prev = AppInit.conf.alloha.filterByYear;
        AppInit.conf.alloha.filterByYear = true;
        try
        {
            return await IndexerSearchEngine.SearchCombinedAsync(
                new IndexerSearchRequest { Query = ImdbId, Season = season, CardMode = false },
                cache,
                jackettSearch: null);
        }
        finally
        {
            AppInit.conf.alloha.filterByYear = prev;
        }
    }

    static string[] Ids(IEnumerable<JacRed.Models.Api.Result> results) =>
        results.Select(r => r.Details.Substring(UrlBase.Length)).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    TorrentDetails Torrent(string id, int relased, int[] seasons, string title) => new TorrentDetails
    {
        url = UrlBase + id,
        trackerName = PickTracker(),
        types = new[] { "serial" },
        title = title,
        name = _ru,
        originalname = _en,
        magnet = "magnet:?xt=urn:btih:" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(id + _en))),
        sid = 10,
        pir = 1,
        sizeName = "1 GB",
        relased = relased,
        seasons = seasons.ToHashSet(),
        createTime = DateTime.UtcNow,
        updateTime = DateTime.UtcNow
    };

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
}
