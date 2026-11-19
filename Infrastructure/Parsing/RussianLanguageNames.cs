using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Parsing
{
    /// <summary>
    /// Language names written in Russian on a tracker page ("русские (софтсаб), английские", "японский")
    /// → ISO 639-2 codes (bibliographic, as Matroska tags carry them).
    /// </summary>
    public static class RussianLanguageNames
    {
        // A stem matches from the start of a word: "русские", "русский", "русск.", but not "белорусские".
        static readonly (Regex stem, string code)[] Stems =
        {
            (Stem("русск"), "rus"), (Stem("украинск"), "ukr"), (Stem("английск"), "eng"),
            (Stem("японск"), "jpn"), (Stem("китайск"), "chi"), (Stem("корейск"), "kor"),
            (Stem("немецк"), "ger"), (Stem("французск"), "fre"), (Stem("испанск"), "spa"),
            (Stem("итальянск"), "ita"), (Stem("португальск"), "por"), (Stem("польск"), "pol"),
            (Stem("тайск"), "tha"), (Stem("вьетнамск"), "vie"), (Stem("индонезийск"), "ind"),
            (Stem("малайск"), "may"), (Stem("арабск"), "ara"), (Stem("турецк"), "tur"),
            (Stem("белорусск"), "bel"), (Stem("казахск"), "kaz"), (Stem("хинди"), "hin"),
            // abbreviations, as whole words: "софт - рус", "яп", "rus (софтсаб)", "jap"
            (Word("рус|rus"), "rus"), (Word("англ|eng"), "eng"), (Word("яп|jap|jpn"), "jpn"),
            (Word("кит|chi"), "chi"), (Word("кор|kor"), "kor")
        };

        static Regex Stem(string stem) =>
            new Regex($"(?<![а-яё]){stem}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        static Regex Word(string words) =>
            new Regex($"(?<![а-яёa-z])({words})(?![а-яёa-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Codes of the languages named in the text; null when it names none ("нет", a list of studios).</summary>
        public static HashSet<string> Codes(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var codes = new HashSet<string>();
            foreach (var (stem, code) in Stems)
            {
                if (stem.IsMatch(text))
                    codes.Add(code);
            }

            return codes.Count > 0 ? codes : null;
        }
    }
}
