using JacRed.Models.Api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JacRed.Engine.Indexers
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

        public static List<Result> FilterByYear(List<Result> items, int year)
        {
            if (year <= 0) return items;
            return items.Where(t =>
            {
                int rel = t.info?.relased ?? 0;
                if (rel <= 0) return true;
                return rel == year || rel == year - 1 || rel == year + 1;
            }).ToList();
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
        static readonly Regex SxxExx = new Regex(@"(?<![0-9])s(?<season>\d{1,2})[\s._-]*e(?<episode>\d{1,3})(?![0-9])", RegexOptions.IgnoreCase);
        static readonly Regex Nxnn = new Regex(@"(?<![0-9])(?<season>\d{1,2})x(?<episode>\d{1,3})(?![0-9])", RegexOptions.IgnoreCase);
        static readonly Regex SeasonPack = new Regex(@"(?<![0-9])s(?<season>\d{1,2})(?!\d|\s*e)(?:\s|\.|\]|/|$|[[(])", RegexOptions.IgnoreCase);

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
                return parsed.Value.season == season && (parsed.Value.episode == null || parsed.Value.episode == episode);
            }

            var release = ParseTitle(t.Title);
            if (release == null) return false;
            if (release.Value.season != season) return false;
            if (!episode.HasValue) return true;
            if (release.Value.isSeasonPack) return true;
            return release.Value.episode == episode;
        }

        static (int season, int? episode, bool isSeasonPack)? ParseTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            var m = SxxExx.Match(title);
            if (m.Success)
                return (int.Parse(m.Groups["season"].Value), int.Parse(m.Groups["episode"].Value), false);
            m = Nxnn.Match(title);
            if (m.Success)
                return (int.Parse(m.Groups["season"].Value), int.Parse(m.Groups["episode"].Value), false);
            m = SeasonPack.Match(title);
            if (m.Success)
                return (int.Parse(m.Groups["season"].Value), null, true);
            return null;
        }

        public static (int? season, int? episode) AttrsFromResult(Result t)
        {
            var parsed = ParseTitle(t.Title);
            if (parsed == null)
            {
                if (t.info?.seasons != null && t.info.seasons.Count == 1)
                    return (t.info.seasons.First(), null);
                return (null, null);
            }
            return (parsed.Value.season, parsed.Value.episode);
        }
    }
}
