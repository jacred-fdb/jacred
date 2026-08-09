using System.Collections.Generic;

namespace JacRed.Infrastructure.Trackers.Anibelka
{
    sealed class AnibelkaCategory
    {
        public string Name { get; init; }
        public string[] Types { get; init; } = new[] { "anime" };
    }

    /// <summary>
    /// Single source of truth for anibelka.com forum sections under «Скачать аниме».
    /// Keep dry_run_anibelka_parser.py in sync.
    /// </summary>
    static class AnibelkaCategories
    {
        public static readonly Dictionary<string, AnibelkaCategory> Map = new()
        {
            ["32"] = new() { Name = "Универсальные" },
            ["33"] = new() { Name = "С озвучкой" },
            ["34"] = new() { Name = "С субтитрами" },
            ["36"] = new() { Name = "Полнометражки" },
            ["37"] = new() { Name = "PSP" },
        };

        public static IEnumerable<string> Ids => Map.Keys;
    }
}
