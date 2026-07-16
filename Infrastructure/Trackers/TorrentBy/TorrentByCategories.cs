using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.TorrentBy
{
    public enum TorrentByTitleKind
    {
        FilmsForeign,
        FilmsRu,
        SerialForeign,
        SerialRu,
        ShowLike,
        Sport
    }

    sealed class TorrentByCategory
    {
        public string[] Types { get; init; }
        public TorrentByTitleKind TitleKind { get; init; }
    }

    /// <summary>
    /// Single source of truth for torrent.by section slugs, JacRed types, and title-parse strategy.
    /// </summary>
    static class TorrentByCategories
    {
        public static readonly Dictionary<string, TorrentByCategory> Map = new()
        {
            // Зарубежные фильмы
            ["films"] = new() { Types = new[] { "movie" }, TitleKind = TorrentByTitleKind.FilmsForeign },
            // Наши фильмы
            ["movies"] = new() { Types = new[] { "movie" }, TitleKind = TorrentByTitleKind.FilmsRu },
            // Зарубежные сериалы
            ["serials"] = new() { Types = new[] { "serial" }, TitleKind = TorrentByTitleKind.SerialForeign },
            // Наши сериалы
            ["series"] = new() { Types = new[] { "serial" }, TitleKind = TorrentByTitleKind.SerialRu },
            // Телевизор / Юмор
            ["tv"] = new() { Types = new[] { "tvshow" }, TitleKind = TorrentByTitleKind.ShowLike },
            ["humor"] = new() { Types = new[] { "tvshow" }, TitleKind = TorrentByTitleKind.ShowLike },
            // Мультфильмы
            ["cartoons"] = new() { Types = new[] { "multfilm", "multserial" }, TitleKind = TorrentByTitleKind.ShowLike },
            // Аниме
            ["anime"] = new() { Types = new[] { "anime" }, TitleKind = TorrentByTitleKind.ShowLike },
            // Спорт
            ["sport"] = new() { Types = new[] { "sport" }, TitleKind = TorrentByTitleKind.Sport },
        };

        public static IEnumerable<string> Ids => Map.Keys;
    }
}
