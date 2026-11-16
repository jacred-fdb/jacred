using System.Collections.Generic;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Xunit;

namespace JacRed.Tests.Indexers;

// Titles are real releases from Russian trackers, as JacRed stores them.
public class SeasonEpisodeFilterTests
{
    static Result Release(string title, params int[] seasons) => new Result
    {
        Title = title,
        info = seasons.Length > 0 ? new TorrentInfo { seasons = new HashSet<int>(seasons) } : null,
    };

    static bool Passes(string title, int season, int? episode, params int[] seasons) =>
        SeasonEpisodeFilter.Filter(new List<Result> { Release(title, seasons) }, season, episode).Count == 1;

    [Theory]
    // SxxEyy, NxMM, Sxx as before
    [InlineData("Кухня /Kuchnia na Maksa /s01e01-20 /HD1080p WEBRip /Полный 1 сезон", 1, 7, true)]
    [InlineData("Кухня /Kuchnia na Maksa /s01e01-20 /HD1080p WEBRip /Полный 1 сезон", 1, 21, false)]
    [InlineData("Кухня /Kuchnia na Maksa /s01e01-20 /HD1080p WEBRip /Полный 1 сезон", 2, null, false)]
    [InlineData("Монолог фармацевта / Записки аптекаря / Kusuriya no Hitorigoto [03x02 из 24] (2026) WEBRip 1080p by ZaLmanVsk", 3, 2, true)]
    [InlineData("Монолог фармацевта / Записки аптекаря / Kusuriya no Hitorigoto [03x02 из 24] (2026) WEBRip 1080p by ZaLmanVsk", 3, 3, false)]
    [InlineData("Провожающая в последний путь Фрирен / Sousou no Frieren [S01] (2023) WEBRip 1080p от KORSARS", 1, 5, true)]
    [InlineData("Провожающая в последний путь Фрирен / Sousou no Frieren [S01] (2023) WEBRip 1080p от KORSARS", 2, null, false)]
    // NxMM with a range of episodes: the requested episode inside the range passes
    [InlineData("Провожающая в последний путь Фрирен / Sousou no Frieren [02x01-04 из 10] (2026) WEBRip 1080p от MoscowGolem", 2, 2, true)]
    [InlineData("Провожающая в последний путь Фрирен / Sousou no Frieren [02x01-04 из 10] (2026) WEBRip 1080p от MoscowGolem", 2, 5, false)]
    // Cyrillic х
    [InlineData("Холод [01х01-07 из 10] (2026) WEB-DLRip-AVC от Generalfilm", 1, 2, true)]
    [InlineData("Холод [01х01-07 из 10] (2026) WEB-DLRip-AVC от Generalfilm", 1, 8, false)]
    [InlineData("Холод [01х01-07 из 10] (2026) WEB-DLRip-AVC от Generalfilm", 2, null, false)]
    // Seasons in words
    [InlineData("Фрирен, провожаюшая в последний путь (2 сезон: 1-10 серии из 10) / Sousou no Frieren / 2026 / ЛМ (AniLiberty), СТ / WEB-DL (1080p)", 2, 5, true)]
    [InlineData("Фрирен, провожаюшая в последний путь (2 сезон: 1-10 серии из 10) / Sousou no Frieren / 2026 / ЛМ (AniLiberty), СТ / WEB-DL (1080p)", 1, null, false)]
    [InlineData("Мажор / Сезон: 2 / Серии: 1-12 из 12 (Николай Булыгин, Максим Полинский) [2016, детектив, криминал, WEBRip 1080p]", 2, 3, true)]
    [InlineData("Мажор / Сезон: 2 / Серии: 1-12 из 12 (Николай Булыгин, Максим Полинский) [2016, детектив, криминал, WEBRip 1080p]", 3, null, false)]
    [InlineData("Беспринципные (2026) WEBRip [H.265/2160p] [4K, SDR, 8-bit] (сезон 5, серии 1-8 из 8)", 5, 3, true)]
    [InlineData("Беспринципные (2026) WEBRip [H.265/2160p] [4K, SDR, 8-bit] (сезон 5, серии 1-8 из 8)", 5, 9, false)]
    [InlineData("Монолог травниці (Сезон 2) / Kusuriya no Hitorigoto (Season 2) (2025) WEBDLRip 1080p H.265 Ukr/Jap", 2, 5, true)]
    [InlineData("Останні з нас /The Last of Us /s02e01-07 /HD720p WEBRip Atmos /2-й сезон повністю (УКР.)", 2, 3, true)]
    // Anime seasons: ТВ-N, 2nd Season
    [InlineData("Записки аптекаря (ТВ-2) / Kusuriya no Hitorigoto 2nd Season / The Apothecary Diaries Season 2 / Монолог фармацевта 2 [TV] [24 из 24] [RUS(int), JAP+Sub] [2025, драма, детектив, мистика, WEB-DL] [1080p]", 2, 5, true)]
    [InlineData("Записки аптекаря (ТВ-2) / Kusuriya no Hitorigoto 2nd Season / The Apothecary Diaries Season 2 / Монолог фармацевта 2 [TV] [24 из 24] [RUS(int), JAP+Sub] [2025, драма, детектив, мистика, WEB-DL] [1080p]", 1, 5, false)]
    [InlineData("Kusuriya no Hitorigoto (2025) | The Apothecary Diaries (2025) | Записки аптекаря [ТВ-2] [2025, TV, 24 эп.] WebRip 720p raw", 2, 5, true)]
    [InlineData("Атака титанов (ТВ-2) / Shingeki no Kyojin Season 2 / Attack on Titan 2nd Season [TV] [12 из 12] [JAP+Sub] [2017, приключения, триллер, фантастика, BDRip] [720p]", 1, null, false)]
    [InlineData("Shingeki no Kyojin: The Final Season (2022) / Атака титанов [ТВ-4, часть 2] (12 из 12) Complete [1080p]", 4, null, true)]
    [InlineData("Провожаюшая в последний путь Фрирен (TB-1) / Sousou no Frieren / Frieren: Beyond Journey's End [TV] [28 из 28] [RUS(int), JAP+Sub] [2023, драма, приключения, фэнтези, WEB-DL] [1080p]", 1, 28, true)]
    // Episodes without a season: season 1
    [InlineData("Провожаюшая в последний путь Фрирен / Sousou no Frieren / Frieren: Beyond Journey's End [TV] [1-9 из 28] [RUS(int), JAP+Sub] [2023, приключения, драма, фэнтези, сенен, WEB-DL] [1080p]", 1, 5, true)]
    [InlineData("Провожаюшая в последний путь Фрирен / Sousou no Frieren / Frieren: Beyond Journey's End [TV] [1-9 из 28] [RUS(int), JAP+Sub] [2023, приключения, драма, фэнтези, сенен, WEB-DL] [1080p]", 1, 10, false)]
    [InlineData("Sousou no Frieren / Фрирен (28 из 28) Complete [1080p]", 1, 5, true)]
    [InlineData("Sousou no Frieren | Frieren: Beyond Journey's End | Провожаюшая в последний путь Фрирен [2023, TV, 28 эп.] WebRip 720p raw", 1, 5, true)]
    [InlineData("Олдскул (1-17 из 17) (2025) WEBRip | AVC | Generalfilm | КПК", 1, 2, true)]
    [InlineData("Полицейский с Рублевки (2016) SATRip (серии 1-8 из 8)", 1, 8, true)]
    [InlineData("One Piece | Ван-Пис [1999, TV, 207-1180 эп.] WEBRip 1080p raw+rus", 1, 1180, true)]
    [InlineData("One Piece | Ван-Пис [1999, TV, 207-1180 эп.] WEBRip 1080p raw+rus", 1, 206, false)]
    // ...so a first-season release without a season mark stays out of season 2
    [InlineData("Монолог фармацевта / Kusuriya no Hitorigoto / Записки аптекаря / The Apothecary Diaries (Норихиро Наганума) [TV] [1-19 из 24] [RUS(int), JAP+Sub] [2023, драма, детектив, исторический, WEB-DL] [1080p]", 2, 5, false)]
    [InlineData("Ore dake Level Up na Ken | Solo Leveling | Поднятие уровня в одиночку [2024, TV, 12 из 12] WEBRip 396p Raw+Rus", 2, 3, false)]
    // Films, OVA, specials and "all seasons" are not guessed as season 1
    [InlineData("Атака Титанов (фильмы) / Shingeki no Kyojin / Attack on Titan [Movie] [1 из 2] [RUS(int), JAP+Sub] [2014, драма, экшен, фэнтези, сенен, BDRip] [720p]", 1, 1, false)]
    [InlineData("Моя геройская академия OVA (1-2 серии из 2) / Boku no Hero Academia / 2016-2017 / ПМ, СТ / WEB-DL (1080p)", 1, 1, false)]
    [InlineData("Атака титанов / Вторжение титанов / Shingeki no Kyojin [Спешл 01 из 02] (2023) WEBRip 1080p от FortunaTV", 1, 1, false)]
    [InlineData("Атака титанов / Shingeki no Kyojin [TV] [Все сезоны, серии 87 из 87] [Без хардсаба] [RUS(int), JAP, Sub] [2013, Сенен, Экшен, Драма, Фэнтези, Детектив]", 2, null, false)]
    // Absolute numbering without a season is left alone
    [InlineData("[SubsPlease] Kusuriya no Hitorigoto - 29 (1080p)", 2, 5, false)]
    // Ranges of seasons: the requested season inside passes, episodes are not checked
    [InlineData("Беспринципные (1-3 сезоны: 1-24 серии из 24) / 2020-2022 / РУ, СТ / WEB-DLRip", 2, 5, true)]
    [InlineData("Беспринципные (1-3 сезоны: 1-24 серии из 24) / 2020-2022 / РУ, СТ / WEB-DLRip", 4, null, false)]
    [InlineData("Записки аптекаря (Монолог фармацевта) (1-2 сезон: 1-48 серии из 48) / Kusuriya no Hitorigoto / 2023-2025 / 2 x ДБ, 5 x ЛМ, СТ / HEVC / BDRip (1080p)", 2, 5, true)]
    [InlineData("Записки аптекаря (Монолог фармацевта) (1-2 сезон: 1-48 серии из 48) / Kusuriya no Hitorigoto / 2023-2025 / 2 x ДБ, 5 x ЛМ, СТ / HEVC / BDRip (1080p)", 3, null, false)]
    [InlineData("Мажор / Сезон: 1-2 / Серии: 1-24 из 24 (Константин Статский, Николай Булыгин) [2014-2016, драма, криминал, WEBRip 1080p]", 2, 20, true)]
    [InlineData("Кухня [S01-S03] (2012-2014) WEB-DLRip, WEB-DLRip-AVC 720p", 3, 4, true)]
    [InlineData("Кухня [S01-S03] (2012-2014) WEB-DLRip, WEB-DLRip-AVC 720p", 4, null, false)]
    [InlineData("Во все тяжкие / Breaking Bad (2008-2012) BDRip [H.264] [сезон 1-4, полностью] [LostFilm]", 4, 2, true)]
    [InlineData("Во все тяжкие / Breaking Bad (2008-2012) BDRip [H.264] [сезон 1-4, полностью] [LostFilm]", 5, null, false)]
    [InlineData("Атака титанов / Вторжение титанов / Shingeki no Kyojin [01-04x01-75 из 87] (2013-2021) WEBRip 720p | StudioBand", 3, 5, true)]
    // Not seasons: resolutions, years, a relay, a dubbing studio
    [InlineData("Железные Дороги Японии [seishun18kinkippu] / Japanese Railways (JR) [2008, любительское видео, CAMRip, 1920x1080]", 19, null, false)]
    [InlineData("Мажор (2025) (ПМ) [WEB-DL] [1080p]", 1, null, false)]
    [InlineData("Эстафета 4x100", 4, null, false)]
    [InlineData("Сериал [12х24 мин] (2020)", 12, null, false)]
    [InlineData("BBC: Планета Земля (диск 2 из 4)", 1, null, false)]
    // The TV-3 channel after the episodes is not season 3
    [InlineData("Секретные материалы (сезон 7, серии 1-22 из 22) ТВ-3", 7, 5, true)]
    [InlineData("Секретные материалы (сезон 7, серии 1-22 из 22) ТВ-3", 3, null, false)]
    [InlineData("Секретные материалы (сезон 7, серии 1-22 из 22) ТВ-3", 1, null, false)]
    [InlineData("Гинтама (ТВ-4: 1-51 из 51)", 4, 20, true)]
    [InlineData("Гинтама (ТВ-4: 1-51 из 51)", 1, null, false)]
    [InlineData("Сериал 01x05 Mindhunter", 1, 5, true)]
    [InlineData("Фантом 1-8 из 8", 1, 3, true)]
    // A later part of a split season numbers episodes from 1: the season passes whole
    [InlineData("Spy x Family (2022) / Семья шпиона [ТВ-1, часть 2] (13 из 13) Complete [1080p]", 1, 20, true)]
    public void Filter_EmptyStoredSeasons_UsesTitle(string title, int season, int? episode, bool expected)
    {
        Assert.Equal(expected, Passes(title, season, episode));
    }

    [Theory]
    // The 2X2 studio is not season 2 episode 2
    [InlineData("Ванпанчмен (1 сезон: 1-12 серии из 12) / One Punch Man / 2015 / ПМ (2X2) / HDRip", 1, 5, true)]
    // The title narrows stored seasons to its episodes
    [InlineData("Поднятие уровня в одиночку (2 сезон: 1-13 серии из 13) / Ore Dake Level Up na Ken / 2025 / ДБ (StudioBand) / WEBRip (1080p)", 2, 13, true)]
    [InlineData("Поднятие уровня в одиночку (2 сезон: 1-13 серии из 13) / Ore Dake Level Up na Ken / 2025 / ДБ (StudioBand) / WEBRip (1080p)", 2, 14, false)]
    [InlineData("Провожаюшая в последний путь Фрирен / Sousou no Frieren [01x01-13 из 28] (2023) WEBRip 1080p от KORSARS", 1, 13, true)]
    // Episodes without a season use the stored season, not season 1
    [InlineData("Ателье колдовских колпаков (1-13 серии из 13) / Tongari Boushi no Atelier / 2026 / ДБ (DEEP) / WEB-DLRip", 2, 3, true)]
    // A title without a season or episodes keeps the stored season
    [InlineData("[uni] Дандадан / Dandadan [2TV][2024-2025, фантастика, приключения, комедия]", 2, 4, true)]
    // Episodes of a later part are not checked
    [InlineData("Атака титанов (Вторжение Гигантов) (3 сезон. Часть 2: 1-10 серии из 10) / Shingeki no Kyojin / 2019 / ДБ (StudioBand) / WEB-DL (1080p)", 3, 15, true)]
    // Not episode ranges: a backwards range, episode 0 of a count
    [InlineData("Сериал / 2024 - 12 из 12", 1, 5, true)]
    [InlineData("Сериал (Сезон 3 из 5) [1-10 из 10]", 3, 5, true)]
    [InlineData("Реинкарнация безработного (2 сезон: 0 серия из 13) / Mushoku Tensei: Isekai Ittara Honki Dasu / 2023 / ДБ, 3 x ЛМ, СТ / WEBRip (1080p)", 2, 5, true)]
    public void Filter_StoredSeason_ChecksEpisodeInTitle(string title, int season, int? episode, bool expected)
    {
        int stored = season;
        Assert.Equal(expected, Passes(title, season, episode, stored));
    }

    [Fact]
    public void Filter_StoredSeasons_TitleOfAnotherSeason_Fails()
    {
        Assert.False(Passes("Провожающая в последний путь Фрирен / Sousou no Frieren [02x01-04 из 10] (2026) WEBRip 1080p от MoscowGolem", 1, 2, 1, 2));
        Assert.False(Passes("Холод [01х01-07 из 10] (2026) WEB-DLRip-AVC от Generalfilm", 2, null, 1));
    }

    [Fact]
    public void Filter_StoredSeasons_PartialSeasonWords_KeepStored()
    {
        // "1, 2 сезон" reads as season 2 only: the stored seasons win over words
        Assert.True(Passes("Мажор (1, 2 сезон: 1-24 серии из 24) / 2014-2016 / РУ / WEBRip", 1, 3, 1, 2));
    }

    [Theory]
    [InlineData("Холод [01х01-07 из 10] (2026) WEB-DLRip-AVC от Generalfilm", 1, true)]
    [InlineData("Холод [01х01-07 из 10] (2026) WEB-DLRip-AVC от Generalfilm", 2, false)]
    [InlineData("Провожаюшая в последний путь Фрирен / Sousou no Frieren / Frieren: Beyond Journey's End [TV] [1-9 из 28] [RUS(int), JAP+Sub] [2023, приключения, драма, фэнтези, сенен, WEB-DL] [1080p]", 1, true)]
    [InlineData("Провожаюшая в последний путь Фрирен / Sousou no Frieren / Frieren: Beyond Journey's End [TV] [1-9 из 28] [RUS(int), JAP+Sub] [2023, приключения, драма, фэнтези, сенен, WEB-DL] [1080p]", 2, false)]
    [InlineData("Беспринципные (1-3 сезоны: 1-24 серии из 24) / 2020-2022 / РУ, СТ / WEB-DLRip", 3, true)]
    [InlineData("Sousou no Frieren (2026) / Фрирен [ТВ-2] (10 из 10) Complete [1080p]", 2, true)]
    [InlineData("[SubsPlease] Sousou no Frieren - 28 (1080p)", 1, false)]
    [InlineData("Третий сезон 1-10 из 10", 5, false)]
    [InlineData(null, 1, false)]
    public void TitleHasSeason(string title, int season, bool expected)
    {
        Assert.Equal(expected, SeasonEpisodeFilter.TitleHasSeason(title, season));
    }

    [Theory]
    [InlineData("Монолог фармацевта / Записки аптекаря / Kusuriya no Hitorigoto [03x02 из 24] (2026) WEBRip 1080p by ZaLmanVsk", 3, 2)]
    // A range of episodes reports the season only
    [InlineData("Холод [01х01-07 из 10] (2026) WEB-DLRip-AVC от Generalfilm", 1, null)]
    [InlineData("Записки аптекаря (ТВ-2) / Kusuriya no Hitorigoto 2nd Season / The Apothecary Diaries Season 2 / Монолог фармацевта 2 [TV] [24 из 24] [RUS(int), JAP+Sub] [2025, драма, детектив, мистика, WEB-DL] [1080p]", 2, null)]
    [InlineData("Провожающая в последний путь Фрирен / Sousou no Frieren [S01] (2023) WEBRip 1080p от KORSARS", 1, null)]
    // Several seasons or no season mark report nothing
    [InlineData("Беспринципные (1-3 сезоны: 1-24 серии из 24) / 2020-2022 / РУ, СТ / WEB-DLRip", null, null)]
    [InlineData("Sousou no Frieren / Фрирен (28 из 28) Complete [1080p]", null, null)]
    [InlineData("Эстафета 4x100", null, null)]
    public void AttrsFromResult_TitleOnly(string title, int? season, int? episode)
    {
        Assert.Equal((season, episode), SeasonEpisodeFilter.AttrsFromResult(Release(title)));
    }

    [Fact]
    public void AttrsFromResult_NoSeasonInTitle_UsesSingleStoredSeason()
    {
        Assert.Equal(((int?)1, (int?)null), SeasonEpisodeFilter.AttrsFromResult(Release("Sousou no Frieren / Фрирен (28 из 28) Complete [1080p]", 1)));
        Assert.Equal(((int?)null, (int?)null), SeasonEpisodeFilter.AttrsFromResult(Release("Sousou no Frieren / Фрирен (28 из 28) Complete [1080p]", 1, 2)));
    }
}
