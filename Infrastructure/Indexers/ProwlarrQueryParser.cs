using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>
    /// Parses Prowlarr Search Feed brace tokens from <c>query</c>
    /// (e.g. <c>{TvdbId:71663} {Season:32}</c>) per Servarr search syntax.
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

        public sealed class Parsed
        {
            public string Query { get; set; }
            public string ImdbId { get; set; }
            public int? Season { get; set; }
            public int? Episode { get; set; }
            public int? Year { get; set; }
            public string Genre { get; set; }
            public string Title { get; set; }
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
                    {
                        hadTvdb = true;
                    }
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

            result.Query = q;
            result.TvdbIdOnly = hadTvdb && string.IsNullOrWhiteSpace(result.Query) && string.IsNullOrWhiteSpace(result.ImdbId);
            return result;
        }
    }
}
