using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.NNMClub
{
    public enum NNMClubTitleKind
    {
        ForeignCinema,
        ForeignSerial,
        RuMovie,
        RuSerial,
        Anime,
        KidsMult,
        ShowLike,
        Sport
    }

    sealed class NNMClubCategory
    {
        public string[] Types { get; init; }

        public NNMClubTitleKind TitleKind { get; init; }

        /// <summary>Cat 7: only parse rows that look like cartoons (мульт / duration).</summary>
        public bool RequireMultInRow { get; init; }

        /// <summary>Cat 7: skip PDF book releases.</summary>
        public bool SkipPdfInTitle { get; init; }
    }

    /// <summary>
    /// Single source of truth for NNMClub portal ids, JacRed types, and title-parse strategy.
    /// Portal: /forum/portal.php?c={id}
    /// </summary>
    static class NNMClubCategories
    {
        public static readonly Dictionary<string, NNMClubCategory> Map = new()
        {
            // 10 - Новинки кино
            ["10"] = new() { Types = new[] { "movie" }, TitleKind = NNMClubTitleKind.ForeignCinema },
            // 13 - Наше кино
            ["13"] = new() { Types = new[] { "movie" }, TitleKind = NNMClubTitleKind.RuMovie },
            // 6  - Зарубежное кино
            ["6"] = new() { Types = new[] { "movie" }, TitleKind = NNMClubTitleKind.ForeignCinema },
            // 11 - HD, UHD и 3D Кино
            ["11"] = new() { Types = new[] { "movie" }, TitleKind = NNMClubTitleKind.ForeignCinema },
            // 4  - Наши сериалы
            ["4"] = new() { Types = new[] { "serial" }, TitleKind = NNMClubTitleKind.RuSerial },
            // 3  - Зарубежные сериалы
            ["3"] = new() { Types = new[] { "serial" }, TitleKind = NNMClubTitleKind.ForeignSerial },
            // 22 - Док. TV-бренды
            ["22"] = new() { Types = new[] { "docuserial", "documovie" }, TitleKind = NNMClubTitleKind.ShowLike },
            // 23 - Док. и телепередачи
            ["23"] = new() { Types = new[] { "docuserial", "documovie" }, TitleKind = NNMClubTitleKind.ShowLike },
            // 1  - Аниме и Манга
            ["1"] = new() { Types = new[] { "anime" }, TitleKind = NNMClubTitleKind.Anime },
            // 7  - Детям и родителям
            ["7"] = new()
            {
                Types = new[] { "multfilm", "multserial" },
                TitleKind = NNMClubTitleKind.KidsMult,
                RequireMultInRow = true,
                SkipPdfInTitle = true
            },
            // 24 - Спорт и активный отдых
            ["24"] = new() { Types = new[] { "sport" }, TitleKind = NNMClubTitleKind.Sport },
            // 21 - Театр, МузВидео, Разное
            ["21"] = new() { Types = new[] { "tvshow" }, TitleKind = NNMClubTitleKind.ShowLike },
            // 27 - Юмор и сатира
            ["27"] = new() { Types = new[] { "tvshow" }, TitleKind = NNMClubTitleKind.ShowLike },
        };

        /// <summary>Portal sections that are not video (music, books, software, games, etc.).</summary>
        public static readonly HashSet<string> NonVideoIds = new()
        {
            "2", "5", "8", "9", "12", "14", "15", "16", "18", "19", "20", "25", "26",
            // temporary WC 2026 event section — not in Map
            "28"
        };

        public static IEnumerable<string> Ids => Map.Keys;
    }
}
