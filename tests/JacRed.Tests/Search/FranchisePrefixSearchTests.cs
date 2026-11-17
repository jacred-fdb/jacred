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
/// kinozal puts the series before the title: "Джеймс Бонд 007: Никогда не говори никогда /
/// James Bond 007: Never Say Never Again / 1983 / …". Neither name equals the film title,
/// so the id search must look behind the prefix.
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

    [Fact]
    public async Task ImdbSearch_FindsPrefixedRelease_NotOtherYearOrSeries()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"alloha:title:{ImdbId}", new AllohaResolveResult
        {
            Search = AllohaOriginal,
            AltName = Ru,
            Year = 1983,
            Type = "movie",
            ImdbId = ImdbId
        }, TimeSpan.FromHours(1));

        var prev = AppInit.conf.alloha.filterByYear;
        AppInit.conf.alloha.filterByYear = true;
        try
        {
            var results = await IndexerSearchEngine.SearchCombinedAsync(
                new IndexerSearchRequest { Query = ImdbId, CardMode = false }, cache, jackettSearch: null);

            Assert.Equal(new[] { "kinozal-4k", "rutor" }, Ids(results));
        }
        finally
        {
            AppInit.conf.alloha.filterByYear = prev;
        }
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
            types = new[] { type },
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
