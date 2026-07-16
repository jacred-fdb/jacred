using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.Kinozal
{
    public enum KinozalTitleKind
    {
        Movie,
        SerialRu,
        SerialEn,
        TvShow
    }

    sealed class KinozalCategory
    {
        public string[] Types { get; init; }
        public KinozalTitleKind TitleKind { get; init; }
    }

    /// <summary>
    /// Single source of truth for Kinozal browse section ids, JacRed types, and title-parse strategy.
    /// </summary>
    static class KinozalCategories
    {
        public static readonly Dictionary<string, KinozalCategory> Map = new()
        {
            // Сериалы
            ["45"] = new() { Types = new[] { "serial" }, TitleKind = KinozalTitleKind.SerialRu },
            ["46"] = new() { Types = new[] { "serial" }, TitleKind = KinozalTitleKind.SerialEn },

            // Фильмы
            ["8"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["6"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["15"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["17"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["35"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["39"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["13"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["14"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["24"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["11"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["9"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["47"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["12"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["10"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["7"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },
            ["16"] = new() { Types = new[] { "movie" }, TitleKind = KinozalTitleKind.Movie },

            // Документальный (films + multi-season docs)
            ["18"] = new() { Types = new[] { "docuserial", "documovie" }, TitleKind = KinozalTitleKind.Movie },

            // Спорт (broadcasts; title shape is movie-like, type must be sport)
            ["37"] = new() { Types = new[] { "sport" }, TitleKind = KinozalTitleKind.Movie },

            // ТВ-шоу
            ["49"] = new() { Types = new[] { "tvshow" }, TitleKind = KinozalTitleKind.TvShow },
            ["50"] = new() { Types = new[] { "tvshow" }, TitleKind = KinozalTitleKind.TvShow },

            // Мульты
            ["21"] = new() { Types = new[] { "multfilm", "multserial" }, TitleKind = KinozalTitleKind.SerialEn },
            ["22"] = new() { Types = new[] { "multfilm", "multserial" }, TitleKind = KinozalTitleKind.SerialRu },

            // Аниме
            ["20"] = new() { Types = new[] { "anime" }, TitleKind = KinozalTitleKind.SerialEn },
        };

        public static IEnumerable<string> Ids => Map.Keys;
    }
}
