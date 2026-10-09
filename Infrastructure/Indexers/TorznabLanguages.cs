using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>
    /// Values of the Torznab <c>language</c> attribute: English language names, one per attribute,
    /// as Jackett emits them and Sonarr/Radarr parse them.
    /// </summary>
    static class TorznabLanguages
    {
        static readonly HashSet<string> UkrainianTrackers = new(StringComparer.OrdinalIgnoreCase) { "toloka", "mazepa" };

        // und / mul / zxx / mis and the private-use range qaa–qtz carry no language.
        static readonly Regex Junk = new Regex(@"^(und|mul|zxx|mis|q[a-t][a-z])$");
        static readonly Regex Code = new Regex(@"^[a-z]{2,3}$");

        // ISO 639-1 and both ISO 639-2 forms (bibliographic / terminologic) → English name.
        static readonly Dictionary<string, string> Names = Table(
            ("Russian", "ru rus"), ("Ukrainian", "uk ukr"), ("English", "en eng"),
            ("Japanese", "ja jpn"), ("Chinese", "zh chi zho"), ("Korean", "ko kor"),
            ("German", "de ger deu"), ("French", "fr fre fra"), ("Spanish", "es spa"),
            ("Italian", "it ita"), ("Portuguese", "pt por"), ("Polish", "pl pol"),
            ("Czech", "cs cze ces"), ("Slovak", "sk slo slk"), ("Hungarian", "hu hun"),
            ("Dutch", "nl dut nld"), ("Greek", "el gre ell"), ("Romanian", "ro rum ron"),
            ("Persian", "fa per fas"), ("Turkish", "tr tur"), ("Hebrew", "he iw heb"),
            ("Arabic", "ar ara"), ("Hindi", "hi hin"), ("Thai", "th tha"),
            ("Vietnamese", "vi vie"), ("Swedish", "sv swe"), ("Norwegian", "no nor nb nob nn nno"),
            ("Danish", "da dan"), ("Finnish", "fi fin"), ("Icelandic", "is ice isl"),
            ("Estonian", "et est"), ("Latvian", "lv lav"), ("Lithuanian", "lt lit"),
            ("Bulgarian", "bg bul"), ("Serbian", "sr srp"), ("Croatian", "hr hrv"),
            ("Bosnian", "bs bos"), ("Slovenian", "sl slv"), ("Macedonian", "mk mac mkd"),
            ("Belarusian", "be bel"), ("Kazakh", "kk kaz"), ("Uzbek", "uz uzb"),
            ("Azerbaijani", "az aze"), ("Georgian", "ka geo kat"), ("Armenian", "hy arm hye"),
            ("Tatar", "tt tat"), ("Indonesian", "id ind"), ("Malay", "ms may msa"),
            ("Tamil", "ta tam"), ("Telugu", "te tel"), ("Malayalam", "ml mal"),
            ("Catalan", "ca cat"));

        static Dictionary<string, string> Table(params (string name, string codes)[] rows)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, codes) in rows)
            {
                map[name.ToLowerInvariant()] = name;
                foreach (var code in codes.Split(' '))
                    map[code] = name;
            }
            return map;
        }

        /// <summary>
        /// A release found only on Ukrainian trackers is Ukrainian first: Cyrillic in its title says nothing
        /// about Russian audio. A merged list with any other tracker ("rutor, toloka") keeps the alphabet guess.
        /// </summary>
        public static bool IsUkrainianTracker(string tracker) =>
            !string.IsNullOrWhiteSpace(tracker) && tracker.Split(',').All(t => UkrainianTrackers.Contains(t.Trim()));

        /// <summary>
        /// The guessed primary language first, then the known ones (tracker, title, voices, ffprobe audio)
        /// in ordinal order. Codes are normalised, duplicates and junk dropped.
        /// </summary>
        public static List<string> Resolve(string primaryCode, IEnumerable<string> known)
        {
            string primary = Name(primaryCode);
            var result = new List<string> { primary };
            if (known == null)
                return result;

            result.AddRange(known
                .Select(Name)
                .Where(n => n != null && n != primary)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal));
            return result;
        }

        /// <summary>English name for a language code; an unknown but well-formed code is returned as is.</summary>
        static string Name(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string code = value.Trim().ToLowerInvariant().Split('-', '_')[0];
            if (Junk.IsMatch(code))
                return null;
            if (Names.TryGetValue(code, out string name))
                return name;
            return Code.IsMatch(code) ? code : null;
        }
    }
}
