using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>
    /// Parses NUM / Lampa-style plain Jackett <c>query</c> strings into card fields
    /// (<c>title</c>, <c>title_original</c>, <c>year</c>) so exact FileDB matching works.
    /// Covers Ru-first (NUM / Lampa <c>lg_df*</c>) and En-first (Lampa <c>df_lg*</c> / clarification).
    /// </summary>
    public static class NumQueryParser
    {
        // "Русское English 1999"
        static readonly Regex RuEnYear = new(
            @"^([^a-zA-Z]+) ([^а-яА-ЯёЁ]+) ((?:19|20)\d{2})$",
            RegexOptions.Compiled);

        // "Русское English"
        static readonly Regex RuEn = new(
            @"^([^a-zA-Z]+) ([^а-яА-ЯёЁ]+)$",
            RegexOptions.Compiled);

        // "English Русское 1999" (Lampa df_lg_year)
        static readonly Regex EnRuYear = new(
            @"^([^а-яА-ЯёЁ]+) ([^a-zA-Z]+) ((?:19|20)\d{2})$",
            RegexOptions.Compiled);

        // "English Русское" (Lampa df_lg)
        static readonly Regex EnRu = new(
            @"^([^а-яА-ЯёЁ]+) ([^a-zA-Z]+)$",
            RegexOptions.Compiled);

        static readonly Regex TrailingYear = new(
            @"^(.+?)\s+((?:19|20)\d{2})$",
            RegexOptions.Compiled);

        public sealed class Parsed
        {
            public string Title { get; set; }
            public string TitleOriginal { get; set; }
            public int Year { get; set; }
            public bool Matched { get; set; }
        }

        /// <summary>
        /// Best-effort parse of NUM/Lampa free-text into title / original / year.
        /// </summary>
        public static Parsed Parse(string query)
        {
            var result = new Parsed();
            if (string.IsNullOrWhiteSpace(query))
                return result;

            string q = query.Trim();

            var slash = IndexerRequestParams.SplitBilingualQuery(q);
            if (!string.IsNullOrWhiteSpace(slash.ru) || !string.IsNullOrWhiteSpace(slash.en))
            {
                string ru = slash.ru;
                string en = slash.en;
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

                result.Title = ru;
                result.TitleOriginal = en;
                result.Matched = !string.IsNullOrWhiteSpace(ru) || !string.IsNullOrWhiteSpace(en);
                return result;
            }

            // "Русское English 1999"
            var mRuEnYear = RuEnYear.Match(q);
            if (mRuEnYear.Success && HasLatinToken(mRuEnYear.Groups[2].Value))
            {
                result.Title = mRuEnYear.Groups[1].Value.Trim();
                result.TitleOriginal = mRuEnYear.Groups[2].Value.Trim();
                if (int.TryParse(mRuEnYear.Groups[3].Value, out int y) && y > 0)
                    result.Year = y;
                result.Matched = true;
                return result;
            }

            // "English Русское 1999"
            var mEnRuYear = EnRuYear.Match(q);
            if (mEnRuYear.Success
                && HasLatinToken(mEnRuYear.Groups[1].Value)
                && HasCyrillicToken(mEnRuYear.Groups[2].Value))
            {
                result.TitleOriginal = mEnRuYear.Groups[1].Value.Trim();
                result.Title = mEnRuYear.Groups[2].Value.Trim();
                if (int.TryParse(mEnRuYear.Groups[3].Value, out int y) && y > 0)
                    result.Year = y;
                result.Matched = true;
                return result;
            }

            // Strip trailing year before bilingual so "Константин 2005" / "Pulp Fiction 1994" work
            string body = q;
            if (TryTakeTrailingYear(q, out string stripped, out int trailingYear) && trailingYear > 0)
            {
                result.Year = trailingYear;
                body = stripped;
            }

            // "Русское English"
            var mRuEn = RuEn.Match(body);
            if (mRuEn.Success && HasLatinToken(mRuEn.Groups[2].Value))
            {
                result.Title = mRuEn.Groups[1].Value.Trim();
                result.TitleOriginal = mRuEn.Groups[2].Value.Trim();
                result.Matched = true;
                return result;
            }

            // "English Русское"
            var mEnRu = EnRu.Match(body);
            if (mEnRu.Success
                && HasLatinToken(mEnRu.Groups[1].Value)
                && HasCyrillicToken(mEnRu.Groups[2].Value))
            {
                result.TitleOriginal = mEnRu.Groups[1].Value.Trim();
                result.Title = mEnRu.Groups[2].Value.Trim();
                result.Matched = true;
                return result;
            }

            if (Regex.IsMatch(body, @"[а-яА-ЯёЁ]"))
                result.Title = body;
            else
                result.TitleOriginal = body;

            result.Matched = !string.IsNullOrWhiteSpace(result.Title)
                || !string.IsNullOrWhiteSpace(result.TitleOriginal)
                || result.Year > 0;
            return result;
        }

        static bool HasLatinToken(string value) =>
            !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "[a-zA-Z]");

        static bool HasCyrillicToken(string value) =>
            !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"[а-яА-ЯёЁ]");

        /// <summary>
        /// When no explicit card titles were provided, promote free-text
        /// <see cref="IndexerSearchRequest.Query"/> into card fields and enable CardMode
        /// (NUM query-only and Lampa clarification). Skips when title/title_original already set.
        /// </summary>
        public static bool ApplyToRequest(IndexerSearchRequest req)
        {
            if (req == null)
                return false;
            if (!string.IsNullOrWhiteSpace(req.Title) || !string.IsNullOrWhiteSpace(req.TitleOriginal))
                return false;
            if (string.IsNullOrWhiteSpace(req.Query))
                return false;

            var parsed = Parse(req.Query);
            if (!parsed.Matched)
                return false;

            if (!string.IsNullOrWhiteSpace(parsed.Title))
                req.Title = parsed.Title;
            if (!string.IsNullOrWhiteSpace(parsed.TitleOriginal))
                req.TitleOriginal = parsed.TitleOriginal;
            if (parsed.Year > 0 && req.Year <= 0)
                req.Year = parsed.Year;

            req.CardMode = IndexerRequestParams.IsCardMetadataSearch(
                req.Title,
                req.TitleOriginal,
                req.IsSerial >= 0 ? req.IsSerial : (int?)null,
                req.Categories,
                req.Genres);

            return req.CardMode;
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
