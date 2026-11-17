using JacRed.Application.Index;
using JacRed.Infrastructure.External;
using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Api;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace JacRed.Tests.Search;

/// <summary>
/// Release names that carry more than the title: a season ("Монолог фармацевта 2 / Kusuriya no
/// Hitorigoto 2nd Season", "… ТВ-2", "… Season 3"), a season subtitle ("Solo Leveling: Arise from
/// the Shadow") or episodes ("Холод (1-10 из 10)"). The id search matches names exactly, so such
/// releases were lost. A season taken from the name narrows: only that season gets the release.
/// </summary>
[Collection("FastDb index")]
public class SeasonTitleFormsSearchTests : IDisposable
{
    const string UrlBase = "https://example.test/season-title-forms/";

    // Digits only, inside the title: a number at the end of a name reads as a season.
    readonly string _tag = (Math.Abs(Guid.NewGuid().GetHashCode()) % 90_000_000 + 10_000_000).ToString();
    readonly List<string> _keys = new();

    string Ru => $"Монолог {_tag} фармацевта";
    string En => $"Kusuriya {_tag} no Hitorigoto";

    public SeasonTitleFormsSearchTests()
    {
        string[] anime = { "anime", "serial" };
        Seed("s1", Ru, En, 2023, anime, $"{Ru} / {En} / 2023 / [WEBRip 1080p][HEVC][1-24]");
        Seed("s2-number", $"{Ru} 2", $"{En} 2nd Season", 2025, anime, $"{Ru} 2 / {En} 2nd Season / 2025 / [WEBRip 1080p][HEVC][1-24]");
        Seed("s2-tv", $"{Ru} ТВ-2", $"{En} TV-2", 2025, anime, $"{Ru} ТВ-2 / {En} TV-2 [24 из 24]");
        Seed("s2-subtitle", $"{En}: Arise from the Shadow", $"Apothecary {_tag}: Arise from the Shadow", 2025, anime,
            $"Apothecary {_tag}: Arise from the Shadow / {En}: Arise from the Shadow / {Ru} [ТВ-2] (13 из 13) Complete [1080p]");
        Seed("s3-word", $"{Ru} (Третий сезон)", $"{En} Season 3", 2026, anime, $"{Ru} (Третий сезон) / {En} Season 3 [03x01-04 из 24] (2026) WEBRip 1080p");
        // Not season 2: a film sequel, a film behind the title, another number.
        Seed("film-2", $"{Ru} 2", $"{En} 2", 2025, new[] { "anime" }, $"{Ru} 2 / {En} 2 (2025) BDRip 1080p");
        Seed("film-subtitle", $"{Ru}: Фильм", $"{En}: The Movie", 2025, new[] { "anime" }, $"{Ru}: Фильм / {En}: The Movie (2025) BDRip");
        Seed("s20", $"{Ru} 20", $"{En} 20", 2025, anime, $"{Ru} 20 / {En} 20 [S01] (2025) WEBRip");
        // The name reads as season 2, the release says season 1.
        Seed("title-s1", $"{Ru} 2", $"{En} 2", 2025, anime, $"{Ru} 2 / {En} 2 [S01] (2025) WEBRip");

        Seed("frost", $"Мороз {_tag} (1-10 из 10)", $"Мороз {_tag} (1-10 из 10)", 2026, new[] { "serial" }, $"Мороз {_tag} (1-10 из 10) (2026) WEBRip");

        Seed("film-parts", $"Ирония {_tag} судьбы (1-2 из 2)", $"Ирония {_tag} судьбы (1-2 из 2)", 1975, new[] { "movie" }, $"Ирония {_tag} судьбы (1-2 из 2) (1975) BDRip");
        Seed("film-brackets", $"Ирония {_tag} судьбы [1-2 серии из 2]", $"Ирония {_tag} судьбы [1-2 серии из 2] (1975) WEB-DL 1080p", 1975, new[] { "movie" },
            $"Ирония {_tag} судьбы [1-2 серии из 2] (1975) WEB-DL 1080p");
        Seed("film-sequel", $"Ирония {_tag} судьбы 2", $"Ирония {_tag} судьбы 2", 1976, new[] { "movie" }, $"Ирония {_tag} судьбы 2 (1976) DVDRip");
        Seed("remake-series", $"Ирония {_tag} судьбы (1-12 из 12)", $"Ирония {_tag} судьбы (1-12 из 12)", 2005, new[] { "serial" }, $"Ирония {_tag} судьбы (1-12 из 12) (2005) DVDRip");

        FastDbIndex.Default.RebuildIfChanged();
    }

    public void Dispose()
    {
        foreach (string key in _keys)
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
    [InlineData(1, new[] { "s1" })]
    [InlineData(2, new[] { "s2-number", "s2-subtitle", "s2-tv" })]
    [InlineData(3, new[] { "s3-word" })]
    [InlineData(4, new string[0])]
    public async Task SeriesIdSearch_FindsSeasonForms_OfThatSeasonOnly(int season, string[] expected)
    {
        var ids = await IdSearch("tt9990101", En, Ru, 2023, "anime", series: true, season);

        Assert.Equal(expected, ids);
    }

    [Theory]
    [InlineData(1, new[] { "frost" })]
    [InlineData(2, new string[0])]
    public async Task SeriesIdSearch_FindsEpisodesInName(int season, string[] expected)
    {
        var ids = await IdSearch("tt9990102", $"Мороз {_tag}", $"Мороз {_tag}", 2026, "serial", series: true, season);

        Assert.Equal(expected, ids);
    }

    [Fact]
    public async Task FilmIdSearch_FindsEpisodesInName_NotSequelOrSeries()
    {
        var ids = await IdSearch("tt9990103", $"Ирония {_tag} судьбы", $"Ирония {_tag} судьбы", 1975, "movie", series: false, season: null);

        Assert.Equal(new[] { "film-brackets", "film-parts" }, ids);
    }

    [Fact]
    public void TitleFormKeys_SubtitlesAndBucketsAreBounded()
    {
        string saga = $"Сага {_tag}", sn = JacRed.Infrastructure.Utils.StringConvert.SearchName(saga);
        string episodes = FileDB.KeyForTorrent($"{saga} (1-5 из 5)", $"{saga} (1-5 из 5)");
        Seed("saga-episodes", $"{saga} (1-5 из 5)", $"{saga} (1-5 из 5)", 2020, new[] { "serial" }, $"{saga} (1-5 из 5) (2020)");
        string subtitle = FileDB.KeyForTorrent($"{saga}: Глава", $"{saga}: Глава");
        Seed("saga-subtitle", $"{saga}: Глава", $"{saga}: Глава", 2020, new[] { "serial" }, $"{saga}: Глава [S01] (2020)");
        FastDbIndex.Default.RebuildIfChanged();

        Assert.Equal(new HashSet<string> { episodes, subtitle }, FastDbIndex.Default.TitleFormKeys(sn, 1));
        Assert.Equal(new HashSet<string> { episodes }, FastDbIndex.Default.TitleFormKeys(sn, 0));

        // A name with many letters behind it gets no subtitle buckets.
        for (int i = 0; i < FastDbIndex.MaxSubtitleParts; i++)
            Seed($"saga-word-{i}", $"{saga}{(char)('а' + i)}", $"{saga}{(char)('а' + i)}", 2020, new[] { "movie" }, $"{saga}{(char)('а' + i)} (2020)");
        FastDbIndex.Default.RebuildIfChanged();
        Assert.Equal(new HashSet<string> { episodes }, FastDbIndex.Default.TitleFormKeys(sn, 1));

        // Too many buckets give nothing.
        for (int i = 0; i < FastDbIndex.MaxTitleFormKeys; i++)
            Seed($"saga-ep-{i}", $"{saga} (1-{i + 6} из 200)", $"{saga} (1-{i + 6} из 200)", 2020, new[] { "serial" }, $"{saga} (1-{i + 6} из 200) (2020)");
        FastDbIndex.Default.RebuildIfChanged();
        Assert.Empty(FastDbIndex.Default.TitleFormKeys(sn, 1));
    }

    async Task<string[]> IdSearch(string imdbId, string search, string altName, int year, string type, bool series, int? season)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"alloha:title:{imdbId}", new AllohaResolveResult
        {
            Search = search,
            AltName = altName,
            Year = year,
            Type = type,
            IsSeries = series,
            ImdbId = imdbId
        }, TimeSpan.FromHours(1));

        var prev = AppInit.conf.alloha.filterByYear;
        AppInit.conf.alloha.filterByYear = true;
        try
        {
            var results = await IndexerSearchEngine.SearchCombinedAsync(
                new IndexerSearchRequest { Query = imdbId, CardMode = false, Season = season }, cache, jackettSearch: null);
            return Ids(results);
        }
        finally
        {
            AppInit.conf.alloha.filterByYear = prev;
        }
    }

    void Seed(string id, string name, string original, int year, string[] types, string title)
    {
        string key = FileDB.KeyForTorrent(name, original);
        if (!_keys.Contains(key))
            _keys.Add(key);

        using var fdb = FileDB.OpenWrite(key);
        fdb.AddOrUpdate(new TorrentDetails
        {
            url = UrlBase + id + "/" + _tag,
            trackerName = PickTracker(),
            types = types,
            title = title,
            name = name,
            originalname = original,
            magnet = "magnet:?xt=urn:btih:" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(id + _tag))),
            sid = 10,
            pir = 1,
            sizeName = "1 GB",
            relased = year,
            createTime = DateTime.UtcNow,
            updateTime = DateTime.UtcNow
        });
    }

    string[] Ids(IEnumerable<Result> results) =>
        results.Where(r => r.Details != null && r.Details.StartsWith(UrlBase) && r.Details.EndsWith("/" + _tag))
            .Select(r => r.Details.Substring(UrlBase.Length, r.Details.Length - UrlBase.Length - _tag.Length - 1))
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

    static string PickTracker()
    {
        var sync = AppInit.conf.synctrackers;
        if (sync != null && sync.Length > 0)
            return sync[0];
        return "kinozal";
    }
}
