using JacRed.Application.Index;
using JacRed.Application.Search;
using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.AppConf;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace JacRed.Tests.Indexers;

/// <summary>
/// "Ru 2019 S01" (AIOStreams: title + year + season) parses into title "Ru 2019" and
/// original "S01". The bare season token must not become a fuzzy query of its own:
/// it matches every bucket whose key contains "s01", i.e. first seasons of other shows.
/// </summary>
public class BareSeasonVariantTests : IDisposable
{
    const string UrlBase = "https://example.test/bare-season-variant/";

    readonly string _otherKey;

    public BareSeasonVariantTests()
    {
        string tag = Guid.NewGuid().ToString("N")[..12];
        // Another show whose original title carries the season token, so its key contains "s01".
        _otherKey = FileDB.KeyForTorrent("Чужой сериал " + tag, "Other Show " + tag + " S01");

        using (var fdb = FileDB.OpenWrite(_otherKey))
        {
            fdb.AddOrUpdate(new TorrentDetails
            {
                url = UrlBase + tag,
                trackerName = PickTracker(),
                types = new[] { "serial" },
                title = $"Чужой сериал {tag} / Other Show {tag} S01 (2019)",
                name = "Чужой сериал " + tag,
                originalname = "Other Show " + tag + " S01",
                magnet = "magnet:?xt=urn:btih:00112233445566778899aabbccddeeff00112233",
                sid = 10,
                pir = 1,
                sizeName = "1 GB",
                relased = 2019,
                createTime = DateTime.UtcNow,
                updateTime = DateTime.UtcNow
            });
        }
        FastDbIndex.Default.RebuildIfChanged();
    }

    public void Dispose()
    {
        try
        {
            using (var fdb = FileDB.OpenWrite(_otherKey))
            {
                fdb.Database.Clear();
                fdb.savechanges = true;
            }
        }
        catch
        {
            // ignore cleanup races
        }

        FileDB.RemoveKeyFromMasterDb(_otherKey);
        try
        {
            string path = FileDB.PathForKey(_otherKey);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    [Theory]
    [InlineData("S01")]
    [InlineData("s1")]
    [InlineData("S01E02")]
    [InlineData("E05")]
    [InlineData("S01-S03")]
    [InlineData("S01E01-E02")]
    [InlineData("S01E01-02")]
    [InlineData("S01.")]
    public void IsSeasonEpisodeOnly_BareTokens(string text)
    {
        Assert.True(IndexerRequestParams.IsSeasonEpisodeOnly(text));
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("Dark S01")]
    [InlineData("1923")]
    [InlineData("4x4")]
    [InlineData("2x2")]
    [InlineData("50x50")]
    [InlineData("4 сезона")]
    [InlineData("12 сезонов")]
    [InlineData("1 сезон любви")]
    [InlineData("Season 1: Origins")]
    [InlineData("знахарь 2019")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSeasonEpisodeOnly_Titles(string text)
    {
        Assert.False(IndexerRequestParams.IsSeasonEpisodeOnly(text));
    }

    [Theory]
    [InlineData("знахарь 2019 S01", "знахарь 2019", "S01")]
    [InlineData("знахарь S01E02", "знахарь", "S01E02")]
    public void BuildQueryVariants_SkipsBareSeasonTitle(string query, string titleRu, string titleEn)
    {
        var variants = IndexerSearchEngine.BuildQueryVariants(query, titleRu, titleEn, new SearchSettings());

        Assert.DoesNotContain(titleEn, variants);
        Assert.Contains(titleRu, variants);
        Assert.Contains("знахарь", variants);
    }

    [Fact]
    public void BuildQueryVariants_KeepsLatinTitle()
    {
        var variants = IndexerSearchEngine.BuildQueryVariants("тьма dark", "тьма", "dark", new SearchSettings());

        Assert.Contains("dark", variants);
        Assert.Contains("тьма", variants);
    }

    [Theory]
    [InlineData("знахарь 2019 S01", "знахарь 2019", "S01")]
    [InlineData("знахарь S01E02", "знахарь", "S01E02")]
    public void V1SearchPairs_SkipsBareSeasonTitle(string query, string titleRu, string titleEn)
    {
        var pairs = IndexerSearchEngine.V1SearchPairs(query, titleRu, titleEn, new SearchSettings(), null);

        Assert.DoesNotContain(pairs, p => p.search == titleEn || p.altname == titleEn);
        Assert.Contains(pairs, p => p.search == titleRu);
    }

    [Fact]
    public void V1SearchPairs_KeepsLatinTitle()
    {
        var pairs = IndexerSearchEngine.V1SearchPairs("тьма dark", "тьма", "dark", new SearchSettings(), null);

        Assert.Contains(("dark", "тьма"), pairs);
        Assert.Contains(("тьма", "dark"), pairs);
    }

    [Theory]
    [InlineData("абвгдежз 2019 S01")]
    [InlineData("абвгдежз S01")]
    [InlineData("абвгдежз S01 2019")]
    [InlineData("абвгдежз 2019 S01.")]
    public async Task CombinedSearch_RuWithoutMatchPlusSeason_DoesNotReturnOtherShows(string query)
    {
        var results = await Search(query);

        Assert.DoesNotContain(results, r => r.Details?.StartsWith(UrlBase) == true);
    }

    [Fact]
    public async Task CombinedSearch_BareSeasonQuery_StillSearchesIt()
    {
        // Control: the fixture is reachable, and an explicit q=S01 keeps its old behaviour.
        var results = await Search("S01");

        Assert.Contains(results, r => r.Details?.StartsWith(UrlBase) == true);
    }

    static async Task<List<JacRed.Models.Api.Result>> Search(string query)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        return await IndexerSearchEngine.SearchCombinedAsync(
            new IndexerSearchRequest { Query = query },
            cache,
            new JackettSearchService(FastDbIndex.Default));
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
}
