using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Persistence
{
    public partial class FileDB
    {
        /// <summary>
        /// Voice names in a release title or an audio track title, in the order they appear.
        /// Words of the title are compared with the voice dictionary (allVoices plus the lists below), the longest
        /// name first and without overlaps, so "Selena International" does not also give "Selena".
        /// With <paramref name="isTitle"/> nothing before the first year counts except a leading "[...]":
        /// there trackers put the title and the director ("Белорусский вокзал (Андрей Смирнов) [1971, ...]").
        /// Shown is false for a common word of the dictionary ("Мосфильм"): not a voice, but it still tells the language.
        /// </summary>
        internal static IEnumerable<(string Name, bool Shown)> MatchVoices(string text, bool isTitle)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            var words = new List<string>();
            var low = new List<string>();
            var seps = new List<string>();
            var starts = new List<int>();
            int pos = 0;
            foreach (Match m in VoiceWord.Matches(text))
            {
                if (words.Count > 0)
                    seps.Add(text.Substring(pos, m.Index - pos));
                words.Add(m.Value);
                low.Add(m.Value.Replace('ё', 'е').Replace('Ё', 'Е').ToLowerInvariant());
                starts.Add(m.Index);
                pos = m.Index + m.Length;
            }
            seps.Add(text.Substring(pos));

            int n = words.Count;
            int year = 0;
            if (isTitle)
            {
                while (year < n && !(words[year].Length == 4 && (words[year].StartsWith("19", StringComparison.Ordinal) || words[year].StartsWith("20", StringComparison.Ordinal)) && words[year].All(char.IsAsciiDigit)))
                    year++;
                if (year == n)
                    year = 0;
            }
            int lead = text.Length - text.TrimStart().Length;
            int leadEnd = isTitle && text[lead] == '[' ? text.IndexOf(']', lead) : -1;

            for (int i = 0; i < n;)
            {
                string key = low[i];
                string hit = VoiceDictionary.Names.ContainsKey(key) && (!VoiceDictionary.Exact.TryGetValue(key, out string written) || written == words[i]) ? key : null;
                // "НТВ+": a "+" right after a word is no word of its own
                if (seps[i].StartsWith('+') && VoiceDictionary.Names.ContainsKey(key + " плюс"))
                    hit = key + " плюс";

                int len = 1;
                if (VoiceDictionary.FirstWords.Contains(low[i]))
                {
                    for (int j = i + 1; j < n && j - i < VoiceDictionary.MaxWords; j++)
                    {
                        string join = VoiceJoin(seps[j - 1]);
                        if (join == null)
                            break;

                        key += join + low[j];
                        if (VoiceDictionary.Names.ContainsKey(key))
                        {
                            hit = key;
                            len = j - i + 1;
                        }
                    }
                }

                if (hit == null)
                {
                    i++;
                    continue;
                }

                if (i >= year || starts[i] < leadEnd)
                {
                    string name = VoiceDictionary.Names[hit];
                    if (name != string.Empty)
                        yield return (name, true);
                    else
                    {
                        // in an audio track title some of them are the studio: "MVO (Карусель)", "Дубляж IVI"
                        name = VoiceDictionary.Hidden[hit];
                        yield return (name, !isTitle && voiceTrackStudios.Contains(name));
                    }
                }

                i += len;
            }
        }

        /// <summary>
        /// The name a voice is shown under: "FoxLife" -> "Fox Life", "Сербин" -> "Ю. Сербин". Unknown names are returned as is.
        /// </summary>
        internal static string CanonicalVoice(string name)
        {
            string key = VoiceKey(name);
            if (key == null || !VoiceDictionary.Names.TryGetValue(key, out string shown))
                return name;

            return shown != string.Empty ? shown : VoiceDictionary.Hidden[key];
        }

        static readonly Regex VoiceWord = new Regex(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

        /// <summary>
        /// How two words of a name are joined in a dictionary key: "+" ("1+1"), a space, or null if a group of the
        /// title ends between them ("|", ",", "/", brackets, " + ").
        /// </summary>
        static string VoiceJoin(string sep)
        {
            if (sep == "+")
                return "+";

            return sep.IndexOfAny(VoiceGroupEnd) < 0 ? " " : null;
        }

        static readonly char[] VoiceGroupEnd = "|/\\,;()[]{}@+".ToCharArray();

        /// <summary>
        /// Dictionary key of a voice name: lower case, ё -> е, words joined by a space ("+" between two words stays,
        /// "+" at the end is " плюс": "НТВ+"); null if the name holds a group end and so can never match.
        /// </summary>
        static string VoiceKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string s = name.Replace('ё', 'е').Replace('Ё', 'Е').ToLowerInvariant();
            var key = new StringBuilder();
            int pos = 0;
            foreach (Match m in VoiceWord.Matches(s))
            {
                if (key.Length > 0)
                {
                    string join = VoiceJoin(s.Substring(pos, m.Index - pos));
                    if (join == null)
                        return null;
                    key.Append(join);
                }
                key.Append(m.Value);
                pos = m.Index + m.Length;
            }

            if (key.Length == 0)
                return null;

            return s.TrimEnd().EndsWith('+') ? key + " плюс" : key.ToString();
        }

        static class VoiceDictionary
        {
            /// <summary>Key -> name shown; "" for a common word that is known but never shown.</summary>
            internal static readonly Dictionary<string, string> Names = new();

            /// <summary>Key of a common word -> its name, for the language and for <see cref="voiceTrackStudios"/>.</summary>
            internal static readonly Dictionary<string, string> Hidden = new();

            /// <summary>Keys matched only as written: as words they are something else ("Deep", "Fox").</summary>
            internal static readonly Dictionary<string, string> Exact = new();

            /// <summary>First words of the keys of several words.</summary>
            internal static readonly HashSet<string> FirstWords = new();

            internal static readonly int MaxWords;

            internal static readonly HashSet<string> Rus, Ukr;

            static VoiceDictionary()
            {
                var shown = new Dictionary<string, string>();
                foreach (var a in voiceAliases)
                {
                    foreach (string s in a.Value)
                        shown[s] = a.Key;
                }

                foreach (string raw in voiceAliases.Keys.Concat(allVoices).Concat(voiceAliases.Values.SelectMany(s => s)))
                {
                    string x = WebUtility.HtmlDecode(raw);
                    if (voiceDropped.Contains(x))
                        continue;

                    string key = VoiceKey(x);
                    if (key == null || Names.ContainsKey(key))
                        continue;

                    Names[key] = voiceStopWords.Contains(x) ? string.Empty : shown.GetValueOrDefault(x, x);
                    if (Names[key] == string.Empty)
                        Hidden[key] = shown.GetValueOrDefault(x, x);
                    if (voiceExact.Contains(x))
                        Exact[key] = x;
                }

                foreach (string key in Names.Keys)
                {
                    string[] w = key.Split(' ', '+');
                    if (w.Length > 1)
                        FirstWords.Add(w[0]);
                    MaxWords = Math.Max(MaxWords, w.Length);
                }

                Rus = rusVoices.Select(CanonicalVoice).ToHashSet();
                Ukr = ukrVoices.Select(CanonicalVoice).ToHashSet();
            }
        }

        /// <summary>
        /// Trackers of one voice studio: their releases are voiced by it even when the title does not say so.
        /// </summary>
        static readonly Dictionary<string, string> voiceTrackers = new Dictionary<string, string>
        {
            ["lostfilm"] = "LostFilm",
            ["hdrezka"] = "HDRezka",
            ["baibako"] = "BaibaKo",
            ["rudub"] = "RuDub",
            ["anidub"] = "AniDUB",
            ["anistar"] = "AniStar",
            ["aniliberty"] = "AniLiberty",
            ["leproduction"] = "LE-Production",
        };

        /// <summary>
        /// Common words that are the studio in an audio track title: TV channels, film studios and online cinemas that order
        /// dubbing ("MVO (Карусель)", "Дубляж IVI"). In a release title they stay hidden: there IVI is the source of the rip.
        /// </summary>
        static readonly HashSet<string> voiceTrackStudios = new HashSet<string>
        {
            "Карусель", "Домашний", "Пятница", "Tycoon", "Twister", "Союзмультфильм", "Україна", "Кинопоиск", "IVI"
        };

        /// <summary>
        /// Short names of allVoices that are words, release tags ("ТВ-3" is the third TV season of an anime) or too rare
        /// to tell from one, and phrases that are a voice type, not a studio.
        /// </summary>
        static readonly HashSet<string> voiceDropped = new HashSet<string>
        {
            "Laci", "ICG", "Vano", "Oni", "Jade", "Andy", "Нота", "AMC", "НСТ", "Че!", "MGM", "МИР", "Твин", "AOS",
            "CPIG", "D1", "Dice", "Gits", "jept", "KIHO", "Line", "MCA", "R5", "SGEV", "TB5", "Tori", "Troy", "Twix",
            "VHS", "ГКГ", "ИГМ", "Инис", "Ирэн", "К9", "ТРК", "КiT", "NLO", "НЛО", "ТВ3", "ТВ-3", "ТВ6"
        };

        /// <summary>
        /// Common words, titles, film studios and release groups: known, so that they are not read as a shorter name,
        /// but never shown.
        /// </summary>
        static readonly HashSet<string> voiceStopWords = new HashSet<string>
        {
            "Levelin", "DeMon", "Discovery", "Description", "Gemini", "Shaman", "Lupin", "Macross", "Elysium",
            "Ворон", "Карусель", "Пятница", "Перец", "Альянс", "Домашний", "Россия", "Советский", "Акцент",
            "Оверлорд", "Twister", "VendettA", "madrid", "Sengoku", "Kobayashi", "Акира", "Rumble",
            "Tycoon", "Good People", "Пирамида", "Гризли", "Superbit", "Супербит", "Мосфильм", "Ленфильм",
            "Союзмультфильм", "Paradox", "Живаго", "Сокуров", "Ракурс", "Парадиз", "ELEKTRI4KA", "Ultradox",
            "National Geographic", "RG.Paravozik", "Україна", "Украина", "Ukraina",
            "Русский дубляж",
            // online cinemas: in a release title the source of the rip, like NF or AMZN
            "Кинопоиск", "Kinopoisk", "Kinopoisk HD", "IVI"
        };

        /// <summary>
        /// Matched only as written: as words they are something else ("Deep", "Fox"), or short names of audio tracks
        /// ("LF", "HDr").
        /// </summary>
        static readonly HashSet<string> voiceExact = new HashSet<string>
        {
            "FOX", "JAM", "AMS", "DEEP", "ТеТ", "TET", "HDr", "TVS", "LF", "NS", "SRb", "СБ", "CPI", "OPT", "HTB"
        };

        /// <summary>
        /// Names allVoices lacks and other spellings of one name: name shown -> its spellings. A name of allVoices listed
        /// here is shown under the name on the left. People are "И. Фамилия".
        /// </summary>
        static readonly Dictionary<string, string[]> voiceAliases = new Dictionary<string, string[]>
        {
            ["JAM"] = new[] { "JAM", "JAM Club" },
            ["DEEP"] = new[] { "DEEP" },
            ["AniLiberty"] = new[] { "AniLiberty" },
            ["AniBaza"] = new[] { "AniBaza" },
            ["Force Media"] = new[] { "Force Media", "ForceMedia" },
            ["Dream Cast"] = new[] { "Dream Cast", "DreamCast" },
            ["Clan Kaizoku"] = new[] { "Clan Kaizoku" },
            ["Kachur"] = new[] { "Kachur" },
            ["InariDuB"] = new[] { "InariDuB" },
            ["Amanogawa"] = new[] { "Amanogawa" },
            ["Bravo Records"] = new[] { "Bravo Records", "Bravo Records Georgia" },
            ["Fox Crime"] = new[] { "Fox Crime", "FoxCrime" },
            ["Movie Dubbing"] = new[] { "Movie Dubbing" },
            ["HotVoice"] = new[] { "HotVoice" },
            ["LineFilm"] = new[] { "LineFilm" },
            ["AniLibria"] = new[] { "AniLibria", "AniLibria.TV" },
            ["AniPlague"] = new[] { "AniPLague" },
            ["Crunchyroll"] = new[] { "Crunchyroll", "Сrunchyroll" },
            ["Omicron"] = new[] { "Omicron", "Omikron", "Омикрон", "Омікрон" },
            ["Fox Life"] = new[] { "Fox Life", "FoxLife" },
            ["SHIZA Project"] = new[] { "SHIZA Project", "SHIZA" },
            ["Hamster Studio"] = new[] { "Hamster Studio", "Hamster", "HamsterStudio" },
            ["Jetvis Studio"] = new[] { "Jetvis Studio", "Jetvis" },
            ["Zone Vision Studio"] = new[] { "Zone Vision Studio", "Zone Vision" },
            ["DeadLine Studio"] = new[] { "DeadLine Studio", "DeadLine" },
            ["Voice Project Studio"] = new[] { "Voice Project Studio", "Voice Project" },
            ["Selena International"] = new[] { "Selena International", "Selena", "Селена Интернешнл", "Селена Интернэшнл" },
            ["Ozz"] = new[] { "Ozz", "Ozz.tv", "Ozz TV" },
            ["Sunshine Studio"] = new[] { "Sunshine Studio", "SunshineStudio" },
            ["Cactus Team"] = new[] { "Cactus Team", "CactusTeam" },
            ["Victory-Films"] = new[] { "Victory-Films", "VictoryFilms" },
            ["Train Studio"] = new[] { "Train Studio", "TrainStudio" },
            ["Melodic Voice Studio"] = new[] { "Melodic Voice Studio", "MelodicVoiceStudio" },
            ["AAA-Sound"] = new[] { "AAA-Sound", "AAASound", "ААА-sound" },
            ["Vulpes Vulpes"] = new[] { "Vulpes Vulpes", "VulpesVulpes" },
            ["HiWay Grope"] = new[] { "HiWay Grope", "HiWayGrope" },
            ["Rain Death"] = new[] { "Rain Death", "RainDeath" },
            ["Dream Records"] = new[] { "Dream Records", "DreamRecords" },
            ["Jimmy J."] = new[] { "Jimmy J.", "JimmyJ" },
            ["1001cinema"] = new[] { "1001cinema", "1001 cinema" },
            ["2x2"] = new[] { "2x2", "2х2" },
            ["TV1000"] = new[] { "TV1000", "TV 1000" },
            ["Так Треба Продакшн"] = new[] { "Так Треба Продакшн", "ТакТребаПродакшн", "Tak Treba Production", "Tak Treba" },
            ["Цікава Ідея"] = new[] { "Цікава Ідея", "Cikava Ideya" },
            ["Три Крапки"] = new[] { "Три Крапки", "3 крапки" },
            ["UkraineFastDUB"] = new[] { "UkraineFastDUB", "UFDUB" },
            ["Postmodern"] = new[] { "Postmodern", "Postmodern Postproduction" },
            ["Red Head Sound"] = new[] { "Red Head Sound", "RHS" },
            ["To4ka"] = new[] { "To4ka", "To4kaTV" },
            ["Кравец"] = new[] { "Кравец", "Kravec", "Kravets", "Kravec Records" },
            ["РенТВ"] = new[] { "РенТВ", "Рен ТВ", "Ren TV", "RenTV" },
            ["Novamedia"] = new[] { "Novamedia", "Новамедиа" },
            ["Колобок"] = new[] { "Колобок", "Kолобок", "Студия Колобок" },
            ["Omskbird"] = new[] { "Omskbird", "Omskbird Records" },
            ["Kerob"] = new[] { "Kerob", "KerobTV" },
            ["Flarrow Films"] = new[] { "Flarrow Films", "FlarrowFilms" },
            ["Ю. Сербин"] = new[] { "Ю. Сербин", "Сербин", "Юрий Сербин", "Serbin", "Yu.Serbin", "Y.Serbin", "SRb" },
            ["М. Яроцкий"] = new[] { "М. Яроцкий", "Яроцкий", "Михаил Яроцкий", "Yarotsky", "M.Yarotsky" },
            ["М. Чадов"] = new[] { "М. Чадов", "Чадов", "Михаил Чадов", "Chadov", "M.Chadov" },
            ["Д. Есарев"] = new[] { "Д. Есарев", "Есарев", "Дмитрий Есарев", "Esarev", "Yesarev", "D.Esarev", "D.Yesarev" },
            ["А. Гаврилов"] = new[] { "А. Гаврилов", "Гаврилов", "Андрей Гаврилов", "Gavrilov", "A.Gavrilov" },
            ["Ю. Живов"] = new[] { "Ю. Живов", "Живов", "Юрий Живов", "Zhivov", "Yu.Zhivov", "Jivov" },
            ["Д. Пучков"] = new[] { "Д. Пучков", "Пучков", "Дмитрий Пучков", "Гоблин", "Goblin" },
            ["Кубик в Кубе"] = new[] { "Кубик в Кубе", "KvK", "Kubik-V-Kube", "Kubik V Kube", "Kubik3", "kubik&ko", "Кубик в Кубе & Ko" },
            ["LeDoyen"] = new[] { "LeDoyen", "Le Doyen" },
            ["А. Матвеев"] = new[] { "А. Матвеев", "Матвеев", "Matveev", "Doctor Joker", "Dr. Joker", "Doctor Jocker" },
            ["А. Дольский"] = new[] { "А. Дольский", "Дольский", "Андрей Дольский", "Dolsky" },
            ["Пифагор"] = new[] { "Пифагор", "Pythagor", "Pifagor" },
            ["Переводман"] = new[] { "Переводман", "Perevodman" },
            ["П. Гланц"] = new[] { "П. Гланц", "Гланц", "Пётр Гланц", "Петр Гланц", "Glanz", "Glanc", "P.Glanc", "P.Glantz", "P.Glanz" },
            ["Л. Володарский"] = new[] { "Л. Володарский", "Володарский", "Леонид Володарский", "L.Volodarsky", "Volodarsky", "Volodarskiy" },
            ["С. Визгунов"] = new[] { "С. Визгунов", "Визгунов", "Сергей Визгунов", "Vizgunov" },
            ["В. Горчаков"] = new[] { "В. Горчаков", "Горчаков", "Василий Горчаков", "Gorchakov" },
            ["В. Дохалов"] = new[] { "В. Дохалов", "Дохалов", "Вартан Дохалов" },
            ["А. Михалёв"] = new[] { "А. Михалёв", "Михалев", "Алексей Михалёв", "Mihalev", "Mikhalev" },
            ["Г. Либергал"] = new[] { "Г. Либергал", "Либергал", "Григорий Либергал", "Libergal" },
            ["Ю. Немахов"] = new[] { "Ю. Немахов", "Немахов", "Юрий Немахов", "Yu.Nemahov", "Nemahov", "Nemakhov" },
            ["А. Толстобров"] = new[] { "А. Толстобров", "Толстобров", "A.Tolstobrov", "Tolstobrov" },
            ["С. Рябов"] = new[] { "С. Рябов", "Рябов", "Сергей Рябов", "Ryabov" },
            ["К. Поздняков"] = new[] { "К. Поздняков", "Поздняков", "Pozdnyakov", "K.Pozdnyakov" },
            ["Ю. Товбин"] = new[] { "Ю. Товбин", "Товбин", "Tovbin" },
            ["Е. Лурье"] = new[] { "Е. Лурье", "Лурье", "Евгения Лурье", "E. Lur'e" },
            ["Н. Дроздов"] = new[] { "Н. Дроздов", "Дроздов", "Николай Дроздов" },
            ["Time Media Group"] = new[] { "Time Media Group", "Тайм Медиа Групп" },
            ["3df voice"] = new[] { "3df voice", "3df" },
            ["Filiza Studio"] = new[] { "Filiza Studio", "Filiza" },
            ["Е. Гаевский"] = new[] { "Е. Гаевский", "Гаевский" },
            ["А. Карповский"] = new[] { "А. Карповский", "Карповский" },
            ["А. Алексеев"] = new[] { "А. Алексеев", "Алексеев" },
            ["А. Багичев"] = new[] { "А. Багичев", "Багичев", "Bagichev" },
            ["В. Завгородний"] = new[] { "В. Завгородний", "Завгородний", "V.Zavgorodny" },
            ["П. Карцев"] = new[] { "П. Карцев", "Карцев", "P.Kartsev" },
            ["А. Кашкин"] = new[] { "А. Кашкин", "Кашкин" },
            ["А. Киреев"] = new[] { "А. Киреев", "Киреев" },
            ["С. Кузнецов"] = new[] { "С. Кузнецов", "Кузнецов" },
            ["В. Курдов"] = new[] { "В. Курдов", "Курдов" },
            ["М. Латышев"] = new[] { "М. Латышев", "Латышев", "Максим Латышев" },
            ["А. Марченко"] = new[] { "А. Марченко", "Марченко", "A.Marchenko" },
            ["Д. Нурмухаметов"] = new[] { "Д. Нурмухаметов", "Нурмухаметов", "D. Nurmukhametov" },
            ["В. Попов"] = new[] { "В. Попов", "Попов", "V.Popov" },
            ["Е. Рудой"] = new[] { "Е. Рудой", "Рудой" },
            ["В. Рукин"] = new[] { "В. Рукин", "Рукин" },
            ["В. Сонькин"] = new[] { "В. Сонькин", "Сонькин" },
            ["Д. Строев"] = new[] { "Д. Строев", "Строев" },
            ["А. Тимофеев"] = new[] { "А. Тимофеев", "Тимофеев" },
            ["К. Филонов"] = new[] { "К. Филонов", "Филонов" },
            ["Интер Фильм"] = new[] { "Интер Фильм", "Интерфильм", "INTERFILM" },
            ["Позитив-Мультимедиа"] = new[] { "Позитив-Мультимедиа", "Позитив", "Позитив Мультимедиа", "Positive Multimedia", "Pozitiv Multimedia", "Pozitiv" },
            ["НТВ+"] = new[] { "НТВ+", "НТВ Плюс" },
            ["НТВ"] = new[] { "НТВ", "HTB" },
            ["НТВ-Профит"] = new[] { "НТВ-Профит", "НТВ Профит" },
            ["ОРТ"] = new[] { "ОРТ", "ORT", "OPT" },
            ["Киномания"] = new[] { "Киномания", "Kinomania", "Kinomaniya", "Kinomanija", "Kinomanya" },
            ["Амальгама"] = new[] { "Амальгама", "Amalgama" },
            ["Невафильм"] = new[] { "Невафильм", "Nevafilm" },
            ["Кураж-Бамбей"] = new[] { "Кураж-Бамбей", "Kuraj-Bambey", "Kurazh-Bambey", "Kuraj Bambey" },
            ["Кинопоиск"] = new[] { "Кинопоиск", "Kinopoisk", "Kinopoisk HD" },
            ["Logos"] = new[] { "Logos", "Логос" },
            ["Русский Бестселлер"] = new[] { "Русский Бестселлер", "Бестселлер" },
            ["Tycoon"] = new[] { "Tycoon Studio", "Tycoon-Studio", "Тайкун" },
            ["Карусель"] = new[] { "Karusel" },
            ["Домашний"] = new[] { "Domashniy" },
            ["Дубляжная"] = new[] { "Дубляжная", "ТО Дубляжная", "TO Dublyazhnaya", "TO Dublyajnaya" },
            ["HDRezka"] = new[] { "HDRezka", "HDRezka Studio", "HDr" },
            ["TVShows"] = new[] { "TVShows", "TVS" },
            ["LostFilm"] = new[] { "LostFilm", "LostFilmTV", "Lost Film", "LF" },
            ["NewStudio"] = new[] { "NewStudio", "NS" },
            ["Студийная Банда"] = new[] { "Студийная Банда", "Studio Band", "StudioBand", "СБ" },
            ["CPI Films"] = new[] { "CPI Films", "CPI" },
            ["НЛО-TV"] = new[] { "НЛО-TV", "NLO TV", "NLO.TV" },
            ["Mallorn Studio"] = new[] { "Mallorn Studio", "Mallorn" },
            ["Україна"] = new[] { "Україна", "Украина", "Ukraina" },
            ["Інтер"] = new[] { "Інтер", "інтер", "IНТЕР", "Inter", "Интер" },
        };
    }
}
