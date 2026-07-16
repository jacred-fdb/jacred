using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>
    /// Parses Prowlarr Search Feed brace tokens from <c>query</c>
    /// (e.g. <c>{TvdbId:71663} {Season:32}</c>) per Servarr search syntax,
    /// and promotes Lampa-style plain queries into Jackett card fields
    /// (title / title_original / year).
    /// </summary>
    public static class ProwlarrQueryParser
    {
        // Named groups match Prowlarr NewznabRequest.QueryToParams (IgnoreCase).
        static readonly Regex TvRegex = new(
            @"\{((?:imdbid\:)(?<imdbid>[^{]+)|(?:rid\:)(?<rid>[^{]+)|(?:tvdbid\:)(?<tvdbid>[^{]+)|(?:tmdbid\:)(?<tmdbid>[^{]+)|(?:tvmazeid\:)(?<tvmazeid>[^{]+)|(?:doubanid\:)(?<doubanid>[^{]+)|(?:season\:)(?<season>[^{]+)|(?:episode\:)(?<episode>[^{]+)|(?:year\:)(?<year>[^{]+)|(?:genre\:)(?<genre>[^{]+))\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex MovieRegex = new(
            @"\{((?:imdbid\:)(?<imdbid>[^{]+)|(?:doubanid\:)(?<doubanid>[^{]+)|(?:tmdbid\:)(?<tmdbid>[^{]+)|(?:traktid\:)(?<traktid>[^{]+)|(?:year\:)(?<year>[^{]+)|(?:genre\:)(?<genre>[^{]+))\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex MusicRegex = new(
            @"\{((?:artist\:)(?<artist>[^{]+)|(?:album\:)(?<album>[^{]+)|(?:track\:)(?<track>[^{]+)|(?:label\:)(?<label>[^{]+)|(?:year\:)(?<year>[^{]+)|(?:genre\:)(?<genre>[^{]+))\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex BookRegex = new(
            @"\{((?:author\:)(?<author>[^{]+)|(?:publisher\:)(?<publisher>[^{]+)|(?:title\:)(?<title>[^{]+)|(?:year\:)(?<year>[^{]+)|(?:genre\:)(?<genre>[^{]+))\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Lampa parse_lang / Jackett NUM: "Русское English 1999" or "Русское English"
        static readonly Regex RuEnYear = new(
            @"^([^a-zA-Z]+) ([^а-яА-ЯёЁ]+) ((?:19|20)\d{2})$",
            RegexOptions.Compiled);

        static readonly Regex RuEn = new(
            @"^([^a-zA-Z]+) ([^а-яА-ЯёЁ]+)$",
            RegexOptions.Compiled);

        static readonly Regex TrailingYear = new(
            @"^(.+?)\s+((?:19|20)\d{2})$",
            RegexOptions.Compiled);

        public sealed class Parsed
        {
            public string Query { get; set; }
            public string ImdbId { get; set; }
            public int? Season { get; set; }
            public int? Episode { get; set; }
            public int? Year { get; set; }
            public string Genre { get; set; }
            public string Title { get; set; }
            public string TitleOriginal { get; set; }
            public string Artist { get; set; }
            public string Album { get; set; }
            public bool TvdbIdOnly { get; set; }
        }

        public static Parsed Parse(string query, string type)
        {
            var result = new Parsed { Query = query };
            if (string.IsNullOrWhiteSpace(query))
                return result;

            string t = (type ?? "search").Trim().ToLowerInvariant();
            string q = query;
            bool hadTvdb = false;

            if (t is "tvsearch" or "tv")
            {
                foreach (Match match in TvRegex.Matches(q))
                {
                    if (match.Groups["tvdbid"].Success)
                        hadTvdb = true;
                    if (match.Groups["imdbid"].Success)
                        result.ImdbId = IndexerRequestParams.NormalizeImdbId(match.Groups["imdbid"].Value);
                    if (match.Groups["season"].Success && int.TryParse(match.Groups["season"].Value, out int season) && season > 0)
                        result.Season = season;
                    if (match.Groups["episode"].Success && int.TryParse(match.Groups["episode"].Value, out int episode) && episode > 0)
                        result.Episode = episode;
                    if (match.Groups["year"].Success && int.TryParse(match.Groups["year"].Value, out int year) && year > 0)
                        result.Year = year;
                    if (match.Groups["genre"].Success)
                        result.Genre = match.Groups["genre"].Value.Trim();
                    q = q.Replace(match.Value, "");
                }
            }
            else if (t is "movie" or "moviesearch")
            {
                foreach (Match match in MovieRegex.Matches(q))
                {
                    if (match.Groups["imdbid"].Success)
                        result.ImdbId = IndexerRequestParams.NormalizeImdbId(match.Groups["imdbid"].Value);
                    if (match.Groups["year"].Success && int.TryParse(match.Groups["year"].Value, out int year) && year > 0)
                        result.Year = year;
                    if (match.Groups["genre"].Success)
                        result.Genre = match.Groups["genre"].Value.Trim();
                    q = q.Replace(match.Value, "");
                }
            }
            else if (t == "music")
            {
                foreach (Match match in MusicRegex.Matches(q))
                {
                    if (match.Groups["artist"].Success)
                        result.Artist = match.Groups["artist"].Value.Trim();
                    if (match.Groups["album"].Success)
                        result.Album = match.Groups["album"].Value.Trim();
                    if (match.Groups["year"].Success && int.TryParse(match.Groups["year"].Value, out int year) && year > 0)
                        result.Year = year;
                    if (match.Groups["genre"].Success)
                        result.Genre = match.Groups["genre"].Value.Trim();
                    q = q.Replace(match.Value, "");
                }
            }
            else if (t == "book")
            {
                foreach (Match match in BookRegex.Matches(q))
                {
                    if (match.Groups["title"].Success)
                        result.Title = match.Groups["title"].Value.Trim();
                    if (match.Groups["author"].Success && string.IsNullOrWhiteSpace(result.Title))
                        result.Title = match.Groups["author"].Value.Trim();
                    if (match.Groups["year"].Success && int.TryParse(match.Groups["year"].Value, out int year) && year > 0)
                        result.Year = year;
                    if (match.Groups["genre"].Success)
                        result.Genre = match.Groups["genre"].Value.Trim();
                    q = q.Replace(match.Value, "");
                }
            }

            q = IndexerRequestParams.NormalizeQuery(q?.Trim());
            if (string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(result.ImdbId))
                q = result.ImdbId;
            if (string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(result.Title))
                q = result.Title;
            if (string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(result.Artist))
                q = string.IsNullOrWhiteSpace(result.Album) ? result.Artist : $"{result.Artist} {result.Album}";

            // Lampa Prowlarr card search: plain query only — promote into Jackett card fields.
            if (!string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(result.ImdbId))
                EnrichPlainQuery(result, q);

            result.Query = q;
            result.TvdbIdOnly = hadTvdb && string.IsNullOrWhiteSpace(result.Query) && string.IsNullOrWhiteSpace(result.ImdbId);
            return result;
        }

        /// <summary>
        /// Best-effort parse of Lampa <c>params.search</c> combinations into title / original / year
        /// so card exact-match works like Jackett (<c>title</c> + <c>title_original</c> + <c>year</c>).
        /// </summary>
        static void EnrichPlainQuery(Parsed result, string q)
        {
            var slash = IndexerRequestParams.SplitBilingualQuery(q);
            if (!string.IsNullOrWhiteSpace(slash.ru) || !string.IsNullOrWhiteSpace(slash.en))
            {
                string ru = slash.ru;
                string en = slash.en;
                if (!result.Year.HasValue)
                {
                    if (TryTakeTrailingYear(ru, out var ruStripped, out int y) && y > 0)
                    {
                        result.Year = y;
                        ru = ruStripped;
                    }
                    else if (TryTakeTrailingYear(en, out var enStripped, out y) && y > 0)
                    {
                        result.Year = y;
                        en = enStripped;
                    }
                }
                result.Title ??= ru;
                result.TitleOriginal ??= en;
                return;
            }

            // "Русское English 1999"
            var mYear = RuEnYear.Match(q);
            if (mYear.Success && Regex.IsMatch(mYear.Groups[2].Value, "[a-zA-Z0-9]{2}"))
            {
                result.Title ??= mYear.Groups[1].Value.Trim();
                result.TitleOriginal ??= mYear.Groups[2].Value.Trim();
                if (!result.Year.HasValue && int.TryParse(mYear.Groups[3].Value, out int y) && y > 0)
                    result.Year = y;
                return;
            }

            // "Русское English"
            var mRuEn = RuEn.Match(q);
            if (mRuEn.Success && Regex.IsMatch(mRuEn.Groups[2].Value, "[a-zA-Z0-9]{2}"))
            {
                result.Title ??= mRuEn.Groups[1].Value.Trim();
                result.TitleOriginal ??= mRuEn.Groups[2].Value.Trim();
                return;
            }

            string single = q;
            if (!result.Year.HasValue && TryTakeTrailingYear(q, out string stripped, out int trailingYear) && trailingYear > 0)
            {
                result.Year = trailingYear;
                single = stripped;
            }

            // Single-language query → use as card title for exact match.
            if (string.IsNullOrWhiteSpace(result.Title) && string.IsNullOrWhiteSpace(result.TitleOriginal))
            {
                if (Regex.IsMatch(single, @"[а-яА-ЯёЁ]"))
                    result.Title = single;
                else
                    result.TitleOriginal = single;
            }
        }

        static bool TryTakeTrailingYear(string value, out string stripped, out int year)
        {
            stripped = value;
            year = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var m = TrailingYear.Match(value.Trim());
            if (!m.Success || !int.TryParse(m.Groups[2].Value, out year) || year < 1900 || year > 2100)
            {
                year = 0;
                return false;
            }

            stripped = m.Groups[1].Value.Trim();
            return !string.IsNullOrWhiteSpace(stripped);
        }
    }
}
