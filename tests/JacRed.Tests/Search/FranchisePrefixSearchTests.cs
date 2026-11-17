using JacRed.Application.Index;
using JacRed.Application.Search;
using JacRed.Infrastructure.External;
using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Api;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace JacRed.Tests.Search;

/// <summary>
/// kinozal puts the series before the title: "Джеймс Бонд 007: Никогда не говори никогда /
/// James Bond 007: Never Say Never Again / 1983 / …". Neither name equals the film title,
/// so the id search and the text search must look behind the prefix.
/// </summary>
[Collection("FastDb index")]
public class FranchisePrefixSearchTests : IDisposable
{
    const string ImdbId = "tt0086006";
    const string UrlBase = "https://example.test/franchise-prefix/";

    // Digits only: a Latin tag would turn a Russian query into "Русское English".
    readonly string _tag = (Math.Abs(Guid.NewGuid().GetHashCode()) % 90_000_000 + 10_000_000).ToString();
    readonly List<string> _keys = new();

    string Ru => "Никогда не говори «никогда» " + _tag;
    string En => "Never Say Never Again " + _tag;
    // Alloha's original name for tt0086006: only the Russian name can match.
    string AllohaOriginal => "James Bond 04.5 - Never Say Never Again " + _tag;

    public FranchisePrefixSearchTests()
    {
        Seed("kinozal-4k", $"Джеймс Бонд 007: Никогда не говори никогда {_tag}", $"James Bond 007: Never Say Never Again {_tag}", 1983, "movie",
            " / 1983 / 5 x ПМ, ПД, 4 x АП, СТ / 4K, HEVC, SDR / WEB-DL (2160p)");
        Seed("rutor", $"Никогда не говори никогда {_tag}", En, 1983, "movie", " (1983) HDRip");
        // Same title behind another prefix, but another year or a series.
        Seed("other-year", $"Тайный агент: Никогда не говори никогда {_tag}", $"Secret Agent: Never Say Never Again {_tag}", 2005, "movie", " (2005) WEB-DL");
        Seed("next-year", $"Шпионские игры: Никогда не говори никогда {_tag}", $"Spy Games: Never Say Never Again {_tag}", 1984, "movie", " (1984) VHSRip");
        Seed("series", $"Шпионы: Никогда не говори никогда {_tag}", $"Spies: Never Say Never Again {_tag}", 1983, "serial", " (1983) 1-6 серии");
        // Only the Russian name is the title: another film for the id search.
        Seed("ru-only", $"Мертвые как я: Никогда не говори никогда {_tag}", $"Dead Like Me: Life After Death {_tag}", 1983, "movie", " (1983) DVD-5");
        // No ':' — the name is not a prefix plus the title.
        Seed("no-colon", $"Сказка никогда не говори никогда {_tag}", $"Tale Never Say Never Again {_tag}", 1983, "movie", " (1983) DVDRip");
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
    [InlineData(null)]
    [InlineData(2000)]
    public async Task ImdbSearch_FindsPrefixedRelease_NotOtherYearOrSeries(int? category)
    {
        // With a category FilterByType is skipped: the franchise match itself must drop the series.
        var cats = category.HasValue ? new List<int> { category.Value } : null;

        var ids = await ImdbSearch(ImdbId, AllohaOriginal, Ru, 1983, cats);

        Assert.Equal(new[] { "kinozal-4k", "rutor" }, ids);
    }

    [Fact]
    public async Task ImdbSearch_SameYearSequelSubtitle_IsAKnownFalseMatch()
    {
        Seed("trunk", $"Багажник: Запертая {_tag}", $"Trunk: Locked In {_tag}", 2023, "movie", " (2023) WEB-DLRip");
        Seed("locked-in", $"Запертая {_tag}", $"Locked In {_tag}", 2023, "movie", " (2023) WEB-DL");
        FastDbIndex.Default.RebuildIfChanged();

        var ids = await ImdbSearch("tt9990001", $"Locked In {_tag}", $"Запертая {_tag}", 2023);

        // Known risk: "Trunk: Locked In" is another 2023 film, but both of its names are
        // "Запертая / Locked In" behind a prefix, so nothing in the names tells them apart.
        Assert.Equal(new[] { "locked-in", "trunk" }, ids);
    }

    [Fact]
    public async Task ImdbSearch_TitleEndingTooManyBuckets_GetsNoFranchiseMatches()
    {
        for (int i = 0; i <= FastDbIndex.MaxFranchiseKeys; i++)
            Seed($"many-{i}", $"Серия {i}: Огонь и лёд {_tag}", $"Series {i}: Fire and Ice {_tag}", 2001, "movie", " (2001) DVDRip");
        Seed("fire", $"Огонь и лёд {_tag}", $"Fire and Ice {_tag}", 2001, "movie", " (2001) DVDRip");
        FastDbIndex.Default.RebuildIfChanged();

        var ids = await ImdbSearch("tt9990002", $"Fire and Ice {_tag}", $"Огонь и лёд {_tag}", 2001);

        Assert.Equal(new[] { "fire" }, ids);
    }

    [Fact]
    public async Task ImdbSearch_OneName_KeepsAPrefixOnOneNameOnly()
    {
        // Alloha may give only the original name. The Russian name of the record has no prefix,
        // and it is still the film, unlike "Багажник / Trunk: Locked In" for a text query.
        Seed("gf-en-prefix", $"Голдфингер {_tag}", $"James Bond 007: Goldfinger {_tag}", 1964, "movie", " (1964) BDRip");
        FastDbIndex.Default.RebuildIfChanged();

        var ids = await ImdbSearch("tt9990003", $"Goldfinger {_tag}", null, 1964);

        Assert.Equal(new[] { "gf-en-prefix" }, ids);
    }

    async Task<string[]> ImdbSearch(string imdbId, string search, string altName, int year, List<int> categories = null)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"alloha:title:{imdbId}", new AllohaResolveResult
        {
            Search = search,
            AltName = altName,
            Year = year,
            Type = "movie",
            ImdbId = imdbId
        }, TimeSpan.FromHours(1));

        var prev = AppInit.conf.alloha.filterByYear;
        AppInit.conf.alloha.filterByYear = true;
        try
        {
            var results = await IndexerSearchEngine.SearchCombinedAsync(
                new IndexerSearchRequest { Query = imdbId, CardMode = false, Categories = categories }, cache, jackettSearch: null);
            return Ids(results);
        }
        finally
        {
            AppInit.conf.alloha.filterByYear = prev;
        }
    }

    [Theory]
    [InlineData("Never Say Never Again {0} 1983", "kinozal-4k rutor")]
    [InlineData("Никогда не говори никогда {0} 1983", "kinozal-4k ru-only rutor")]
    public async Task TextSearchWithYear_FindsPrefixedRelease_NotOtherYearOrSeries(string query, string expected)
    {
        var results = await TextSearch(string.Format(query, _tag));

        // One title: the text search cannot tell "ru-only" from the film. A prefixed
        // release of the next year or a series is left out, though an exact name would stay.
        Assert.Equal(expected.Split(' '), Ids(results));
    }

    [Theory]
    [InlineData("The Empire Strikes Back {0} 1980")]
    [InlineData("Империя наносит ответный удар {0} 1980")]
    public async Task TextSearchWithYear_PrefixedMatch_KeepsTheFallbackWithoutYear(string query)
    {
        // No exact name, so the card search falls back to the title without the year, as a part
        // of a name: that finds "Episode V - The Empire Strikes Back". A prefixed release of the
        // year must not stand in for it.
        Seed("sw-prefix", $"Звёздные войны: Империя наносит ответный удар {_tag}", $"Star Wars: The Empire Strikes Back {_tag}", 1980, "movie", " (1980) BDRip");
        Seed("sw-episode", $"Звёздные войны: Эпизод 5 - Империя наносит ответный удар {_tag}", $"Star Wars: Episode V - The Empire Strikes Back {_tag}", 1980, "movie", " (1980) BDRip");
        FastDbIndex.Default.RebuildIfChanged();

        var results = await TextSearch(string.Format(query, _tag));

        Assert.Equal(new[] { "sw-episode", "sw-prefix" }, Ids(results));
    }

    [Theory]
    // A show of the film year: "Вызов / The Challenge" is a subtitle of "Squid Game".
    [InlineData("Игра в кальмара: Вызов", "Squid Game: The Challenge", "tvshow", "The Challenge")]
    [InlineData("Игра в кальмара: Вызов", "Squid Game: The Challenge", "docuserial documovie", "Вызов")]
    // A TV making-of of the film year.
    [InlineData("Большой киночарт: Вызов", "Большой киночарт: Вызов", "docuserial documovie", "Вызов")]
    // Another film of the year: "Багажник", whose original name has the title as its subtitle.
    [InlineData("Багажник", "Trunk: The Challenge", "movie", "The Challenge")]
    public async Task TextSearchWithYear_PrefixedMatch_NeedsAFilmWithAPrefixOnBothNames(string name, string original, string types, string title)
    {
        Seed("film", $"Вызов {_tag}", $"The Challenge {_tag}", 2023, "movie", " (2023) WEB-DL");
        Seed("other", $"{name} {_tag}", $"{original} {_tag}", 2023, types, " (2023) WEB-DL");
        FastDbIndex.Default.RebuildIfChanged();

        var results = await TextSearch($"{title} {_tag} 2023");

        Assert.Equal(new[] { "film" }, Ids(results));
    }

    [Fact]
    public async Task TextSearchWithYear_TitleEndingTooManyBuckets_GetsNoFranchiseMatches()
    {
        for (int i = 0; i <= FastDbIndex.MaxFranchiseKeys; i++)
            Seed($"many-{i}", $"Серия {i}: Огонь и лёд {_tag}", $"Series {i}: Fire and Ice {_tag}", 2001, "movie", " (2001) DVDRip");
        Seed("fire", $"Огонь и лёд {_tag}", $"Fire and Ice {_tag}", 2001, "movie", " (2001) DVDRip");
        FastDbIndex.Default.RebuildIfChanged();

        var results = await TextSearch($"Fire and Ice {_tag} 2001");

        Assert.Equal(new[] { "fire" }, Ids(results));
    }

    [Theory]
    [InlineData(2, null)]
    [InlineData(0, 5000)]
    public async Task TextSearchWithYear_SeriesCard_GetsNoFranchiseMatches(int isSerial, int? category)
    {
        // The franchise pass takes films; "anime" is a film type too, and a series card takes
        // that type, so the pass would add the series "Клинок: Вызов".
        Seed("series", $"Вызов {_tag}", $"The Challenge {_tag}", 2023, "serial", " (2023) [1-8 из 8]");
        Seed("anime", $"Клинок: Вызов {_tag}", $"Blade: The Challenge {_tag}", 2023, "anime", " (2023) [1-12 из 12]");
        FastDbIndex.Default.RebuildIfChanged();

        var results = await TextSearch($"The Challenge {_tag} 2023", isSerial, category.HasValue ? new List<int> { category.Value } : null);

        Assert.Equal(new[] { "series" }, Ids(results));
    }

    [Fact]
    public async Task TextSearchWithYear_SameYearSubtitleOnOneName_IsAKnownFalseMatch()
    {
        Seed("film", $"Тёмная сторона {_tag}", $"Bloodline {_tag}", 2018, "movie", " (2018) WEB-DL");
        Seed("dead", $"День мертвецов: Злая кровь {_tag}", $"Day of the Dead: Bloodline {_tag}", 2018, "movie", " (2018) BDRip");
        FastDbIndex.Default.RebuildIfChanged();

        var results = await TextSearch($"Bloodline {_tag} 2018");

        // Known risk: "Day of the Dead: Bloodline" is another 2018 film, but it has the title
        // behind a prefix and a prefix on the other name, as kinozal names a Bond film.
        Assert.Equal(new[] { "dead", "film" }, Ids(results));
    }

    [Fact]
    public async Task TextSearchWithoutYear_KeepsExactNamesOnly()
    {
        var results = await TextSearch("Never Say Never Again " + _tag);

        Assert.Equal(new[] { "rutor" }, Ids(results));
    }

    [Fact]
    public void FranchiseKeys_LongerPartsOnly_AndEmptyOverTheLimit()
    {
        string ru = StringConvertName(Ru), en = StringConvertName(En);
        string exact = FileDB.KeyForTorrent($"Никогда не говори никогда {_tag}", En);
        string ruOnly = FileDB.KeyForTorrent($"Мертвые как я: Никогда не говори никогда {_tag}", $"Dead Like Me: Life After Death {_tag}");
        var prefixed = _keys.Where(k => k != exact).ToHashSet();
        var byOriginal = prefixed.Where(k => k != ruOnly).ToHashSet();

        // Either side may end with the title; the exact rutor bucket is left to ExactKeys.
        Assert.Equal(prefixed, FastDbIndex.Default.FranchiseKeys(ru, en, max: 250));
        Assert.Equal(prefixed, FastDbIndex.Default.FranchiseKeys(ru, null, max: 250));
        Assert.Equal(byOriginal, FastDbIndex.Default.FranchiseKeys(null, en, max: 250));

        Assert.Empty(FastDbIndex.Default.FranchiseKeys(null, en, max: byOriginal.Count - 1));
        Assert.Empty(FastDbIndex.Default.FranchiseKeys("она", "her", max: 250));
    }

    [Theory]
    [InlineData("Джеймс Бонд 007: Голдфингер", "James Bond 007: Goldfinger", "голдфингер", "goldfinger", true)]
    [InlineData("Голдфингер", "James Bond 007: Goldfinger", "голдфингер", "goldfinger", true)]
    [InlineData("Джеймс Бонд: Доктор Ноу", "James Bond: Dr. No", "докторноу", "drno", true)]
    [InlineData("Звёздные войны: Эпизод 5: Империя наносит ответный удар", null, "империянаноситответныйудар", null, true)]
    [InlineData("Джеймс Бонд 007. Голдфингер", "James Bond 007. Goldfinger", "голдфингер", "goldfinger", false)]
    [InlineData("Джеймс Бонд 007: Шаровая молния", "James Bond 007: Thunderball", "голдфингер", "goldfinger", false)]
    [InlineData(": Голдфингер", null, "голдфингер", null, false)]
    [InlineData("Голдфингер", null, "голдфингер", null, false)]
    [InlineData("Исчезновение Элеанор Ригби: Она", "The Disappearance of Eleanor Rigby: Her", "она", "her", false)]
    [InlineData("Джеймс Бонд 007: Никогда не говори никогда", "James Bond 007: Never Say Never Again", "никогданеговориникогда", "jamesbond045neversayneveragain", true)]
    [InlineData("Мертвые как я: Жизнь после смерти", "Dead Like Me: Life After Death", "жизньпослесмерти", "afterlife", false)]
    [InlineData("Мертвые как я: Жизнь после смерти", "Dead Like Me: Life After Death", "жизньпослесмерти", null, true)]
    public void IsFranchiseMatch_LooksBehindAColon(string name, string original, string nameSn, string originalSn, bool expected)
    {
        Assert.Equal(expected, FastDbIndex.IsFranchiseMatch(name, original, nameSn, originalSn));
    }

    [Theory]
    [InlineData("Багажник", "Trunk: Locked In", null, "lockedin", true, false)]
    [InlineData("Голдфингер", "James Bond 007: Goldfinger", null, "goldfinger", true, false)]
    [InlineData("Джеймс Бонд 007: Голдфингер", "Goldfinger", "голдфингер", null, true, false)]
    [InlineData("Багажник: Запертая", "Trunk: Locked In", null, "lockedin", true, true)]
    [InlineData("Trunk: Locked In", "Trunk: Locked In", null, "lockedin", true, true)]
    [InlineData("Мертвые как я: Жизнь после смерти", null, "жизньпослесмерти", null, true, true)]
    public void IsFranchiseMatch_OneName_OtherNamePrefixedForTextOnly(string name, string original, string nameSn, string originalSn, bool byId, bool byText)
    {
        Assert.Equal(byId, FastDbIndex.IsFranchiseMatch(name, original, nameSn, originalSn));
        Assert.Equal(byText, FastDbIndex.IsFranchiseMatch(name, original, nameSn, originalSn, otherNamePrefixed: true));
    }

    async Task<List<Result>> TextSearch(string query, int isSerial = -1, List<int> categories = null)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        return await IndexerSearchEngine.SearchCombinedAsync(
            new IndexerSearchRequest { Query = query, IsSerial = isSerial, Categories = categories }, cache, new JackettSearchService(FastDbIndex.Default));
    }

    static string StringConvertName(string value) => JacRed.Infrastructure.Utils.StringConvert.SearchName(value);

    void Seed(string id, string name, string original, int year, string type, string tail)
    {
        string key = FileDB.KeyForTorrent(name, original);
        if (!_keys.Contains(key))
            _keys.Add(key);

        using var fdb = FileDB.OpenWrite(key);
        fdb.AddOrUpdate(new TorrentDetails
        {
            url = UrlBase + id + "/" + _tag,
            trackerName = PickTracker(),
            types = type.Split(' '),
            title = $"{name} / {original}{tail}",
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
