using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.Rutor
{
    public enum RutorTitleKind
    {
        ForeignMovie,
        RuMovie,
        ForeignSerial,
        RuSerial,
        ShowLike
    }

    sealed class RutorCategory
    {
        public string[] Types { get; init; }

        public RutorTitleKind TitleKind { get; init; }

        /// <summary>Cat 17: skip rows without " UKR" in title.</summary>
        public bool RequireUkrInTitle { get; init; }
    }

    /// <summary>
    /// Single source of truth for Rutor browse ids, JacRed types, and title-parse strategy.
    /// </summary>
    static class RutorCategories
    {
        public static readonly Dictionary<string, RutorCategory> Map = new()
        {
            // 1  - Зарубежные фильмы
            ["1"] = new() { Types = new[] { "movie" }, TitleKind = RutorTitleKind.ForeignMovie },
            // 5  - Наши фильмы
            ["5"] = new() { Types = new[] { "movie" }, TitleKind = RutorTitleKind.RuMovie },
            // 4  - Зарубежные сериалы
            ["4"] = new() { Types = new[] { "serial" }, TitleKind = RutorTitleKind.ForeignSerial },
            // 16 - Наши сериалы
            ["16"] = new() { Types = new[] { "serial" }, TitleKind = RutorTitleKind.RuSerial },
            // 12 - Научно-популярные фильмы
            ["12"] = new() { Types = new[] { "docuserial", "documovie" }, TitleKind = RutorTitleKind.ShowLike },
            // 6  - Телевизор
            ["6"] = new() { Types = new[] { "tvshow" }, TitleKind = RutorTitleKind.ShowLike },
            // 7  - Мультипликация
            ["7"] = new() { Types = new[] { "multfilm", "multserial" }, TitleKind = RutorTitleKind.ShowLike },
            // 10 - Аниме
            ["10"] = new() { Types = new[] { "anime" }, TitleKind = RutorTitleKind.ShowLike },
            // 17 - Иностранные релизы (UKR filter)
            ["17"] = new() { Types = new[] { "movie" }, TitleKind = RutorTitleKind.ForeignMovie, RequireUkrInTitle = true },
            // 13 - Спорт и Здоровье
            ["13"] = new() { Types = new[] { "sport" }, TitleKind = RutorTitleKind.ShowLike },
            // 15 - Юмор
            ["15"] = new() { Types = new[] { "tvshow" }, TitleKind = RutorTitleKind.ShowLike },
        };

        public static IEnumerable<string> Ids => Map.Keys;
    }
}
