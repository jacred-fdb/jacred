using System;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>
    /// Release names that hold more than the title, so an exact name match misses them:
    /// episodes in brackets ("Холод (1-10 из 10)", "…А зори здесь тихие [1-2 серии из 2]"),
    /// a season ("Монолог фармацевта 2", "Kusuriya no Hitorigoto 2nd Season", "Jujutsu Kaisen TV-2",
    /// "Магическая битва (Второй сезон)") or a season subtitle ("Solo Leveling: Arise from the Shadow").
    /// A season or subtitle only narrows: it needs a season search, the number in the name must be
    /// that season, and the stored seasons or the title must name it. A subtitle needs season 2 or later:
    /// behind a first season it is a spin-off or a revival ("Побег: Продолжение (1 сезон…)", 2017).
    /// </summary>
    public static class ReleaseTitleForms
    {
        const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        // StringConvert.SearchName forms, index = season
        static readonly string[] SeasonWords = { null, "первый", "второй", "третий", "четвертый", "пятый", "шестой", "седьмой", "восьмой", "девятый", "десятый" };
        static readonly string[] SeasonMarks = { "season", "сезон", "tv", "тв", "tb" };

        // SearchName rest of a name with episodes in brackets: 110из10, 12сериииз2, 2из2расширеннаяверсия
        static readonly Regex EpisodesRest = new(@"^[0-9]+(?:сери[а-я]*)?из[0-9]+[a-zа-я]*$", Opts);

        // (1-10 из 10), [1-2 серии из 2], [01-05 из 05], (2 из 2)
        static readonly Regex EpisodesGroup = new(@"\s*[(\[]\s*[0-9]{1,4}(?:\s*-\s*[0-9]{1,4})?\s*(?:сери\p{L}*\s*)?из\s*[0-9]{1,4}\s*[)\]]", Opts);

        // An optional ": subtitle" may follow the season
        static readonly Regex[] SeasonNames =
        {
            // 2nd Season, Season 2, TV-2, ТВ-2, [ТВ-2], (Сезон 2), 2 сезон, (Второй сезон).
            // Spaces, a bracket or both after the title; atomic, so a run of spaces is not split many ways.
            new(@"^(?<base>.*?\S)(?>\s*)(?:[(\[](?>\s*)|(?<=\s))(?:(?<s>[0-9]{1,2})(?:st|nd|rd|th)\s+season|season\s*(?<s>[0-9]{1,2})|(?:тв|tv|tb)-?\s*(?<s>[0-9]{1,2})|сезон\s*(?<s>[0-9]{1,2})|(?<s>[0-9]{1,2})(?:-?й)?\s+сезон|(?<w>\p{L}+)\s+сезон)\s*[)\]]?(?:\s*:\s*\S.*)?$", Opts),
            // Монолог фармацевта 2, Поднятие уровня в одиночку 2: Восстаньте из тени
            new(@"^(?<base>.*?\S)(?>\s+)(?<s>[0-9]{1,2})(?:\s*:\s*\S.*)?$", Opts),
        };

        // Ore dake Level Up na Ken: Arise from the Shadow
        static readonly Regex Subtitle = new(@"^(?<base>[^:]+?)\s*:\s*\S", Opts);

        /// <summary>
        /// Whether a fastdb part that starts with the title (SearchName) may be one of these forms,
        /// judged by the <paramref name="rest"/> after the title: episodes for any search, and for a
        /// season search that season's number or word. Subtitles are any letters and are judged by the caller.
        /// </summary>
        public static bool IsCandidateRest(string rest, int season)
        {
            if (string.IsNullOrEmpty(rest))
                return false;
            if (EpisodesRest.IsMatch(rest))
                return true;
            if (season <= 0)
                return false;

            string n = season.ToString();
            if (NumberAt(rest, 0, n))
                return true;
            foreach (string mark in SeasonMarks)
            {
                if (rest.StartsWith(mark, StringComparison.Ordinal) && NumberAt(rest, mark.Length, n))
                    return true;
            }
            return season < SeasonWords.Length && rest.StartsWith(SeasonWords[season] + "сезон", StringComparison.Ordinal);
        }

        static bool NumberAt(string s, int at, string n) =>
            string.CompareOrdinal(s, at, n, 0, n.Length) == 0 && (s.Length == at + n.Length || !char.IsDigit(s[at + n.Length]));

        /// <summary>
        /// True when the name or original name of <paramref name="t"/> is <paramref name="sn"/> or
        /// <paramref name="altSn"/> (SearchName) plus episodes in brackets, or, for <paramref name="season"/> &gt; 0,
        /// plus that season or a subtitle, and the release names that season.
        /// </summary>
        public static bool Matches(TorrentDetails t, string sn, string altSn, int season) =>
            NameMatches(t, t.name, sn, altSn, season) || NameMatches(t, t.originalname, sn, altSn, season);

        static bool NameMatches(TorrentDetails t, string name, string sn, string altSn, int season)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            bool isTitle(string part)
            {
                string p = StringConvert.SearchName(part);
                return p != null && (p == sn || p == altSn);
            }

            var episodes = EpisodesGroup.Match(name);
            if (episodes.Success && episodes.Index > 0 && isTitle(name.Substring(0, episodes.Index)))
                return true;

            if (season <= 0)
                return false;

            foreach (var form in SeasonNames)
            {
                var m = form.Match(name);
                if (m.Success && NameSeason(m) == season && isTitle(m.Groups["base"].Value))
                    return NamesSeason(t, season);
            }

            if (season < 2)
                return false;
            var sub = Subtitle.Match(name);
            return sub.Success && isTitle(sub.Groups["base"].Value) && NamesSeason(t, season);
        }

        static int NameSeason(Match m)
        {
            if (m.Groups["s"].Success)
                return int.Parse(m.Groups["s"].Value);
            return Array.IndexOf(SeasonWords, StringConvert.SearchName(m.Groups["w"].Value));
        }

        static bool NamesSeason(TorrentDetails t, int season) =>
            t.seasons != null && t.seasons.Count > 0
                ? t.seasons.Contains(season)
                : SeasonEpisodeFilter.TitleNamesSeason(t.title, season);
    }
}
