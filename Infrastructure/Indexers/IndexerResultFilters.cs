using JacRed.Models.Api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Indexers
{
    public static class IndexerResultFilters
    {
        public const int DefaultLimit = 100;
        public const int MaxLimit = 1000;

        public static List<Result> FilterByCategory(List<Result> items, string catParam)
        {
            if (string.IsNullOrWhiteSpace(catParam)) return items;
            var wanted = new HashSet<int>();
            foreach (var part in catParam.Split(','))
            {
                if (int.TryParse(part.Trim(), out int n)) wanted.Add(n);
            }
            if (wanted.Count == 0) return items;

            bool hasMovie = wanted.Any(c => c >= 2000 && c < 3000);
            bool hasTv = wanted.Any(c => c >= 5000 && c < 6000);
            if (hasMovie && hasTv) return items;

            return items.Where(t =>
            {
                if (t.Category == null || t.Category.Count == 0) return true;
                foreach (var wantedCat in wanted)
                {
                    int bucket = (wantedCat / 1000) * 1000;
                    if (t.Category.Any(c => c >= bucket && c < bucket + 1000)) return true;
                }
                return false;
            }).ToList();
        }

        /// <summary>Year gate of <see cref="MatchesCardYear"/>; series keep later seasons.</summary>
        public static List<Result> FilterByYear(List<Result> items, int year, bool movieLike = true)
        {
            if (year <= 0) return items;
            return items.Where(t =>
            {
                int rel = t.info?.relased ?? 0;
                return MatchesCardYear(rel, year, movieLike);
            }).ToList();
        }

        /// <summary>
        /// Card-search year gate. Unknown <paramref name="relased"/> (≤0) passes.
        /// Movies: year ± 1. Serials and other: relased ≥ year − 1.
        /// </summary>
        public static bool MatchesCardYear(int relased, int year, bool movieLike)
        {
            if (year <= 0 || relased <= 0)
                return true;
            if (movieLike)
                return relased == year || relased == year - 1 || relased == year + 1;
            return relased >= (year - 1);
        }

        /// <summary>Keep items whose info.types contains <paramref name="type"/>; items with null/empty types pass.</summary>
        public static List<Result> FilterByType(List<Result> items, string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return items;
            return items.Where(t =>
            {
                var types = t.info?.types;
                if (types == null || types.Length == 0) return true;
                return types.Contains(type);
            }).ToList();
        }

        public static List<Result> FilterByTrackers(List<Result> items, List<string> trackers)
        {
            if (trackers == null || trackers.Count == 0)
                return items;

            var allowed = TrackerNameMatching.ToAllowSet(trackers);
            return items.Where(t => TrackerNameMatching.Matches(t.Tracker, allowed)).ToList();
        }

        public static List<Result> Paginate(List<Result> items, int? limit, int? offset)
        {
            int off = Math.Max(0, offset ?? 0);
            if (!limit.HasValue && off == 0) return items;
            int lim = limit.HasValue ? Math.Max(0, Math.Min(limit.Value, MaxLimit)) : DefaultLimit;
            return items.Skip(off).Take(lim).ToList();
        }
    }

    public static class SeasonEpisodeFilter
    {
        const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled;

        // S01E05, S01E01-04, S01E01-E04, s01e01-20. A range end may not be a resolution (S01E01-720p).
        static readonly Regex SxxExx = new Regex(@"(?<![0-9])s(?<season>[0-9]{1,2})[\s._-]*e(?<episode>[0-9]{1,3})(?:-(?:s[0-9]{1,2})?e?(?<last>[0-9]{1,3})(?![0-9pi]))?(?![0-9])", Opts);
        // 01x05, 01х01-07 (Cyrillic х), 01-04x01-75 (seasons 1-4); not a duration (12х24 мин).
        static readonly Regex Nxnn = new Regex(@"(?<![0-9])(?<season>[0-9]{1,2})(?:-(?<seasonLast>[0-9]{1,2}))?[x\u0445](?<episode>[0-9]{2,3})(?:-(?<last>[0-9]{1,3})(?![0-9pi]))?(?![0-9])(?!\s*(?:мин|min)(?:ут\p{L}*|utes?)?\.?(?!\p{L}))", Opts);

        // Season words, applied in this order; each match is blanked out before the next form and the episode forms.
        static readonly Regex[] SeasonForms =
        {
            // 2nd Season
            new Regex(@"(?<![0-9])(?<season>[0-9]{1,2})(?:st|nd|rd|th)\s+season", Opts),
            // 2 сезон, 1-3 сезоны, 2-й сезон
            new Regex(@"(?<![0-9])(?<season>[0-9]{1,2})(?:-(?<seasonLast>[0-9]{1,2}))?(?:-?й)?\s*сезон", Opts),
            // Сезон: 4, сезон 5, Сезоны 1-3, Season 2, Season 1-3; not episodes (Третий сезон 1-10 из 10)
            new Regex(@"(?<!\p{L})(?:сезон\p{L}*|season)\s*[:№]?\s*(?<season>[0-9]{1,2})(?:-(?<seasonLast>[0-9]{1,2})(?!\s*из))?(?![0-9]|-(?!й))", Opts),
            // Anime: (ТВ-2), [TV-3], (TB-1), ТВ-3 / …; not the channel at the end (… из 22) ТВ-3)
            new Regex(@"(?<![\p{L}0-9])(?:тв|tv|tb)-(?<season>[0-9]{1,2})(?=[)\],:.]|\s+[/\[-])", Opts),
            // [S02], [S01-S03], [S01-05]
            new Regex(@"(?<![0-9])s(?<season>[0-9]{1,2})(?:-s?(?<seasonLast>[0-9]{1,2}))?(?![0-9]|\s*e)(?:\s|\.|\]|/|$|[[(])", Opts),
        };

        // [1-19 из 24], (28 из 28), 1-8 серии из 8, Серии: 1-16 из 16, [12+1 из 12+1], [1-7 из >12]. "N из K" is episodes 1-N.
        static readonly Regex EpisodesOf = new Regex(@"(?<![0-9.-])(?<!(?<!\p{L})(?:диск|disc|dvd|том|vol)\p{L}*\.?\s*)(?:(?<first>[0-9]{1,4})\s*-{1,2}\s*)?(?<last>[0-9]{1,4})(?:\s*\+\s*[0-9]{1,3})?\s*(?:сери\p{L}*\s*)?из\s*>?\s*[0-9]", Opts);
        // [2023, TV, 28 эп.], [2019, TV, 25 ep], [1999, TV, 207-1180 эп.]
        static readonly Regex EpisodeCount = new Regex(@"(?<![0-9.])(?:(?<first>[0-9]{1,4})-)?(?<last>[0-9]{1,4})\s*(?:эп|ep)(?!\p{L})", Opts);
        // A later part of a split season numbers its episodes from 1 again: (4 сезон. Часть 2: 1-12 серии из 12) is E17-28.
        static readonly Regex LaterPart = new Regex(@"(?<!\p{L})(?:част\p{L}*|part|cour)\s*(?<part>[0-9]{1,2})(?![0-9-])", Opts);
        // Episodes without a season are season 1, unless the title names a season in words or is a film, OVA or special.
        static readonly Regex NoSeasonOneGuess = new Regex(@"(?<!\p{L})(?:(?:сезон|season|фильм|спешл|спэшл|special)\p{L}*|ova|oad|ona|ова|movie|sp|(?:тв|tv|tb)-[0-9])(?!\p{L})", Opts);

        sealed class TitleInfo
        {
            public readonly HashSet<int> Seasons = new HashSet<int>();
            public int? FirstEpisode, LastEpisode;
            public bool FirstSeasonByDefault;
            // Season from SxxEyy or NxMM rather than from words
            public bool ByEpisodeMark;

            public bool HasSeason(int season) =>
                Seasons.Count > 0 ? Seasons.Contains(season) : FirstSeasonByDefault && season == 1;

            // Several seasons number their episodes through, so a multi-season pack is not checked by episode.
            public bool HasEpisode(int episode) =>
                Seasons.Count > 1 || !FirstEpisode.HasValue || (episode >= FirstEpisode && episode <= LastEpisode);
        }

        public static List<Result> Filter(List<Result> items, int season, int? episode)
        {
            if (season <= 0) return items;
            return items.Where(t => Matches(t, season, episode)).ToList();
        }

        static bool Matches(Result t, int season, int? episode)
        {
            var seasons = t.info?.seasons;
            if (seasons != null && seasons.Count > 0)
            {
                if (!seasons.Contains(season)) return false;
                if (!episode.HasValue) return true;
                var parsed = ParseTitle(t.Title);
                if (parsed == null) return true;
                // Words may list seasons in part ("1, 2 сезон"): only SxxEyy or NxMM overrides the stored seasons.
                if (parsed.Seasons.Count > 0 && !parsed.Seasons.Contains(season)) return !parsed.ByEpisodeMark;
                return parsed.HasEpisode(episode.Value);
            }

            var release = ParseTitle(t.Title);
            if (release == null || !release.HasSeason(season)) return false;
            return !episode.HasValue || release.HasEpisode(episode.Value);
        }

        /// <summary>Whether the release title names the season, or lists episodes only and is read as season 1.</summary>
        public static bool TitleHasSeason(string title, int season) => ParseTitle(title)?.HasSeason(season) == true;

        static TitleInfo ParseTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            var info = new TitleInfo();

            var m = SxxExx.Match(title);
            if (!m.Success) m = FirstNxnn(title);
            if (m.Success)
            {
                AddSeasons(info, m);
                info.ByEpisodeMark = true;
                int first = int.Parse(m.Groups["episode"].Value);
                int last = m.Groups["last"].Success ? int.Parse(m.Groups["last"].Value) : first;
                SetEpisodes(info, first, Math.Max(first, last));
                return info;
            }

            string rest = title;
            foreach (var form in SeasonForms)
                rest = form.Replace(rest, s => { AddSeasons(info, s); return new string(' ', s.Length); });

            m = EpisodesOf.Match(rest);
            if (!m.Success) m = EpisodeCount.Match(rest);
            if (m.Success && !IsLaterPart(rest))
                SetEpisodes(info, m.Groups["first"].Success ? int.Parse(m.Groups["first"].Value) : 1, int.Parse(m.Groups["last"].Value));

            if (info.Seasons.Count == 0)
            {
                if (!info.FirstEpisode.HasValue) return null;
                info.FirstSeasonByDefault = !NoSeasonOneGuess.IsMatch(title);
            }
            return info;
        }

        static Match FirstNxnn(string title)
        {
            var m = Nxnn.Match(title);
            // A three-digit episode needs a two-digit season (01x100): "Эстафета 4x100" is not an episode.
            while (m.Success && m.Groups["episode"].Length == 3 && m.Groups["season"].Length == 1)
                m = m.NextMatch();
            return m;
        }

        static void AddSeasons(TitleInfo info, Match m)
        {
            int first = int.Parse(m.Groups["season"].Value);
            var lastGroup = m.Groups["seasonLast"];
            int last = lastGroup.Success ? int.Parse(lastGroup.Value) : first;
            for (int s = first; s <= Math.Max(first, last); s++)
                info.Seasons.Add(s);
        }

        static bool IsLaterPart(string title)
        {
            var m = LaterPart.Match(title);
            return m.Success && int.Parse(m.Groups["part"].Value) >= 2;
        }

        // A backwards range is not episodes: "/ 2024 - 12 из 12", "0 серия из 13".
        static void SetEpisodes(TitleInfo info, int first, int last)
        {
            if (first > last) return;
            info.FirstEpisode = first;
            info.LastEpisode = last;
        }

        public static (int? season, int? episode) AttrsFromResult(Result t)
        {
            var parsed = ParseTitle(t.Title);
            if (parsed != null && parsed.Seasons.Count == 1)
            {
                // The episode attribute holds one episode: a range reports none.
                int? episode = parsed.FirstEpisode == parsed.LastEpisode ? parsed.FirstEpisode : null;
                return (parsed.Seasons.First(), episode);
            }
            if (t.info?.seasons != null && t.info.seasons.Count == 1)
                return (t.info.seasons.First(), null);
            return (null, null);
        }
    }
}
