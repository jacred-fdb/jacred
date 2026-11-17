using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;
using Xunit;

namespace JacRed.Tests.Indexers;

// Names and titles of real releases, as JacRed stores them.
public class ReleaseTitleFormsTests
{
    [Theory]
    // Episodes, for any search
    [InlineData("110из10", 0, true)]
    [InlineData("12сериииз2", 0, true)]
    [InlineData("2из2расширеннаяверсия", 0, true)]
    // A season only for that season
    [InlineData("2", 2, true)]
    [InlineData("2ndseason", 2, true)]
    [InlineData("season2", 2, true)]
    [InlineData("tv2", 2, true)]
    [InlineData("тв2", 2, true)]
    [InlineData("сезон2", 2, true)]
    [InlineData("второйсезон", 2, true)]
    [InlineData("2восстаньтеизтени", 2, true)]
    [InlineData("2", 1, false)]
    [InlineData("2", 0, false)]
    [InlineData("20", 2, false)]
    [InlineData("2023", 2, false)]
    [InlineData("tv20", 2, false)]
    [InlineData("третийсезон", 2, false)]
    // Letters are subtitles, left to the caller
    [InlineData("arisefromtheshadow", 2, false)]
    public void IsCandidateRest(string rest, int season, bool expected)
    {
        Assert.Equal(expected, ReleaseTitleForms.IsCandidateRest(rest, season));
    }

    [Theory]
    // Episodes in the name
    [InlineData("Холод (1-10 из 10)", "Холод (1-10 из 10) (2026) WEBRip | 4К", "Холод", 0, true)]
    [InlineData("...А зори здесь тихие [1-2 серии из 2]", "...А зори здесь тихие [1-2 серии из 2] (1972) WEB-DL 1080p", "...А зори здесь тихие", 0, true)]
    [InlineData("А зори здесь тихие (2 из 2) (Расширенная версия)", "А зори здесь тихие (2 из 2) (Расширенная версия) (1972) DVDRip", "А зори здесь тихие", 0, true)]
    [InlineData("Место встречи изменить нельзя [01-05 из 05]", "Место встречи изменить нельзя [01-05 из 05] (1979) HDTVRip-AVC", "Место встречи изменить нельзя", 1, true)]
    [InlineData("Холодное сердце (1-2 из 2)", "Холодное сердце (1-2 из 2)", "Холод", 0, false)]
    // A season in the name, for that season, when the release names it
    [InlineData("Монолог фармацевта 2", "Монолог фармацевта 2 / Kusuriya no Hitorigoto 2nd Season / 2025 / [WEBRip 1080p][HEVC][1-24]", "Монолог фармацевта", 2, true)]
    [InlineData("Kusuriya no Hitorigoto 2nd Season", "Монолог фармацевта 2 / Kusuriya no Hitorigoto 2nd Season / 2025 / [WEBRip 1080p][HEVC][1-24]", "Kusuriya no Hitorigoto", 2, true)]
    [InlineData("JUJUTSU KAISEN Season 2", "Магическая битва 2 / JUJUTSU KAISEN Season 2 / 2023 / [WEBRip 1080p][HEVC][1-23]", "Jujutsu Kaisen", 2, true)]
    [InlineData("Dandadan ТВ-2", "Дандадан TV-2 / Dandadan ТВ-2 [12 из 12]", "Dandadan", 2, true)]
    [InlineData("Магическая Битва (Второй Сезон)", "Магическая Битва (Второй Сезон) / Jujutsu Kaisen 2nd Season [02x01-04 из 23] (2023) WEB-DLRip 1080p", "Магическая битва", 2, true)]
    [InlineData("Shingeki no Kyojin Season 3", "Shingeki no Kyojin Season 3 / Вторжение гигантов [ТВ-3, часть 1] (12 из 12) Complete [720p]", "Shingeki no Kyojin", 3, true)]
    [InlineData("Поднятие уровня в одиночку 2: Восстаньте из тени", "Поднятие уровня в одиночку 2: Восстаньте из тени / Solo Leveling 2 [02x01-04 из 13] (2025) WEBRip 1080p", "Поднятие уровня в одиночку", 2, true)]
    [InlineData("Монолог фармацевта 2", "Монолог фармацевта 2 / Kusuriya no Hitorigoto 2nd Season / 2025 / [WEBRip 1080p][HEVC][1-24]", "Монолог фармацевта", 1, false)]
    [InlineData("Монолог фармацевта 2", "Монолог фармацевта 2 / Kusuriya no Hitorigoto 2nd Season / 2025 / [WEBRip 1080p][HEVC][1-24]", "Монолог фармацевта", 0, false)]
    // A film sequel does not name a season
    [InlineData("Дюна 2", "Дюна 2 / Dune: Part Two (2024) BDRip 1080p", "Дюна", 2, false)]
    // A subtitle, for a season search, when the release names that season
    [InlineData("Solo Leveling: Arise from the Shadow", "Solo Leveling: Arise from the Shadow / Ore dake Level Up na Ken: Arise from the Shadow / Поднятие уровня в одиночку [ТВ-2] (13 из 13) Complete [1080p]", "Solo Leveling", 2, true)]
    [InlineData("Solo Leveling: Arise from the Shadow", "Solo Leveling: Arise from the Shadow / Ore dake Level Up na Ken: Arise from the Shadow / Поднятие уровня в одиночку [ТВ-2] (13 из 13) Complete [1080p]", "Solo Leveling", 1, false)]
    [InlineData("Атака титанов: финал - заключительная глава", "Атака титанов: финал - заключительная глава / Attack on Titan: The Final Season Part 3", "Атака титанов", 3, false)]
    [InlineData("Побег: Продолжение", "Побег: Продолжение / Prison Break: Sequel (2017) WEB-DLRip", "Побег", 1, false)]
    public void Matches_Name(string name, string title, string search, int season, bool expected)
    {
        var t = new TorrentDetails { name = name, originalname = name, title = title, seasons = new HashSet<int>() };

        Assert.Equal(expected, ReleaseTitleForms.Matches(t, StringConvert.SearchName(search), null, season));
    }

    [Fact]
    public void Matches_StoredSeasonsWinOverTitle()
    {
        var t = new TorrentDetails
        {
            name = "Магическая битва 2",
            originalname = "JUJUTSU KAISEN Season 2",
            title = "Магическая битва 2 / JUJUTSU KAISEN Season 2 [S02] (2023) WEBRip 1080p",
            seasons = new HashSet<int> { 1 }
        };

        Assert.False(ReleaseTitleForms.Matches(t, "магическаябитва", "jujutsukaisen", 2));
        t.seasons = new HashSet<int> { 2 };
        Assert.True(ReleaseTitleForms.Matches(t, "магическаябитва", "jujutsukaisen", 2));
    }
}
