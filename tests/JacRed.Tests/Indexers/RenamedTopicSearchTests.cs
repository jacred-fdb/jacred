using JacRed.Application.Index;
using JacRed.Infrastructure.External;
using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Api;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace JacRed.Tests.Indexers;

/// <summary>
/// A renamed topic: the record with the new title goes to a new bucket, the old record stays in the old one
/// (also under an old domain of the tracker). Both carry the same magnet; the search must show the new title.
/// </summary>
public class RenamedTopicSearchTests : IDisposable
{
    const string ImdbId = "tt9900301";
    static readonly DateTime Renamed = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    readonly string _tag = Guid.NewGuid().ToString("N")[..12];
    readonly List<string> _keys = new();

    string En => "Re Zero Renamed " + _tag;
    string OldRu => "Жизнь с нуля старое " + _tag;
    string NewRu => "Жизнь с нуля новое " + _tag;

    public void Dispose()
    {
        foreach (var key in _keys)
        {
            try
            {
                using (var fdb = FileDB.OpenWrite(key))
                {
                    fdb.Database.Clear();
                    fdb.savechanges = true;
                }
            }
            catch
            {
                // ignore cleanup races
            }

            FileDB.RemoveKeyFromMasterDb(key);
            try
            {
                string path = FileDB.PathForKey(key);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Theory]
    [InlineData(8, 6)]
    [InlineData(6, 8)]
    public async Task TitleComesFromLaterCopy(int oldSeeders, int newSeeders)
    {
        Store(oldTitle: $"{OldRu} / {En} [TV+Special] [25 из 25]", newTitle: $"{NewRu} (ТВ-1) / {En} [TV+Special] [25 из 25]", oldSeeders, newSeeders);

        var result = Assert.Single(await Search(season: null));

        Assert.Equal($"{NewRu} (ТВ-1) / {En} [TV+Special] [25 из 25]", result.Title);
        Assert.Equal(Url("org"), result.Details);
        Assert.Equal(Math.Max(oldSeeders, newSeeders), result.Seeders);
    }

    [Fact]
    public async Task FirstSeasonPack_NotFoundForFourthSeason()
    {
        Store(oldTitle: $"{OldRu} / {En} [TV+Special] [25 из 25]", newTitle: $"{NewRu} (ТВ-1) / {En} [TV+Special] [25 из 25]", 8, 6);

        var results = await Search(season: 4);

        Assert.Empty(SeasonEpisodeFilter.Filter(results, 4, 7));
    }

    [Fact]
    public async Task OldTitleOfSeasonOne_DoesNotBringInLaterCopyOfSeasonTwo()
    {
        // The old title lists episodes only and reads as season 1; the topic is season 2 now.
        Store(oldTitle: $"{OldRu} / {En} [25 из 25]", newTitle: $"{NewRu} (ТВ-2) / {En} [25 из 25]", 8, 6);

        var results = await Search(season: 1);

        Assert.Empty(SeasonEpisodeFilter.Filter(results, 1, null));
        Assert.Single(await Search(season: 2));
    }

    void Store(string oldTitle, string newTitle, int oldSeeders, int newSeeders)
    {
        string tracker = PickTracker();
        Add(OldRu, Torrent(tracker, Url("net"), OldRu, oldTitle, oldSeeders, Renamed));
        Add(NewRu, Torrent(tracker, Url("org"), NewRu, newTitle, newSeeders, Renamed.AddMonths(9)));
        // Exact (id) search reads bucket keys from fastdb.
        FastDbIndex.Default.RebuildIfChanged();
    }

    void Add(string ru, TorrentDetails t)
    {
        string key = FileDB.KeyForTorrent(ru, En);
        if (!_keys.Contains(key)) _keys.Add(key);
        using var fdb = FileDB.OpenWrite(key);
        fdb.AddOrUpdate(t);
    }

    string Url(string domain) => $"https://rutracker.{domain}/forum/viewtopic.php?t={_tag}";

    TorrentDetails Torrent(string tracker, string url, string ru, string title, int seeders, DateTime updateTime) => new()
    {
        url = url,
        trackerName = tracker,
        types = new[] { "anime" },
        title = title,
        name = ru,
        originalname = En,
        magnet = "magnet:?xt=urn:btih:" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(_tag))),
        sid = seeders,
        pir = 1,
        sizeName = "170 GB",
        relased = 2016,
        createTime = Renamed,
        updateTime = updateTime
    };

    async Task<List<Result>> Search(int? season)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"alloha:title:{ImdbId}", new AllohaResolveResult { Search = En, AltName = NewRu, ImdbId = ImdbId }, TimeSpan.FromHours(1));

        return await IndexerSearchEngine.SearchCombinedAsync(
            new IndexerSearchRequest { Query = ImdbId, Season = season, CardMode = false },
            cache,
            jackettSearch: null);
    }

    static string PickTracker()
    {
        var sync = AppInit.conf.synctrackers;
        if (sync != null && sync.Length > 0)
            return sync[0];

        var disabled = AppInit.conf.disable_trackers;
        string candidate = "rutracker";
        if (disabled != null && disabled.Contains(candidate))
            candidate = "kinozal";
        return candidate;
    }
}
