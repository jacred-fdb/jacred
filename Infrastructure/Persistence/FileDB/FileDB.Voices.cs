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

            // some titles write "й" as "и" + U+0306
            text = text.Normalize(NormalizationForm.FormC);

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
            // "Original AC3 (IVI)": the online cinema is the source of the original track, not its studio
            bool original = !isTitle && n > 0 && low[0] is "original" or "оригинал" or "оригінал";

            for (int i = 0; i < n;)
            {
                string key = low[i];
                string hit = VoiceDictionary.Names.ContainsKey(key) && (!VoiceDictionary.Exact.TryGetValue(key, out var written) || written.Contains(words[i])) ? key : null;
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
                        yield return (name, !isTitle && !original && voiceTrackStudios.Contains(name));
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
        /// Dictionary key of a voice name: NFC, lower case, ё -> е, words joined by a space ("+" between two words stays,
        /// "+" at the end is " плюс": "НТВ+"); null if the name holds a group end and so can never match.
        /// </summary>
        static string VoiceKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string s = name.Normalize(NormalizationForm.FormC).Replace('ё', 'е').Replace('Ё', 'Е').ToLowerInvariant();
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

            /// <summary>Keys matched only as written, with their written forms: as words they are something else ("Deep", "Fox").</summary>
            internal static readonly Dictionary<string, HashSet<string>> Exact = new();

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

                // voiceStopWords last: sources such as "Netflix" are in no other list
                foreach (string raw in voiceAliases.Keys.Concat(allVoices).Concat(voiceAliases.Values.SelectMany(s => s)).Concat(voiceStopWords))
                {
                    string x = WebUtility.HtmlDecode(raw).Normalize(NormalizationForm.FormC);
                    if (voiceDropped.Contains(x))
                        continue;

                    string key = VoiceKey(x);
                    if (key == null)
                        continue;

                    // "ТеТ" and "ТЕТ": one key, both written forms allowed
                    if (voiceExact.Contains(x))
                    {
                        if (!Exact.TryGetValue(key, out var written))
                            Exact[key] = written = new HashSet<string>();
                        written.Add(x);
                    }

                    if (Names.ContainsKey(key))
                        continue;

                    Names[key] = voiceStopWords.Contains(x) ? string.Empty : shown.GetValueOrDefault(x, x);
                    if (Names[key] == string.Empty)
                        Hidden[key] = shown.GetValueOrDefault(x, x);
                }

                foreach (string key in Names.Keys)
                {
                    string[] w = key.Split(' ', '+');
                    if (w.Length > 1)
                        FirstWords.Add(w[0]);
                    MaxWords = Math.Max(MaxWords, w.Length);
                }

                Rus = rusVoices.Concat(voiceRus).Select(CanonicalVoice).ToHashSet();
                Ukr = ukrVoices.Concat(voiceUkr).Select(CanonicalVoice).ToHashSet();
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
            "Карусель", "Домашний", "Пятница", "Tycoon", "Twister", "Союзмультфильм", "Україна", "Кинопоиск", "IVI",
            "Россия", "ТВ3", "ТВ6", "AMC", "K1", "Новий Канал", "НЛО-TV", "Enter-фільм", "Дім", "Нота", "Марафон",
            "Okko", "KION", "Megogo", "Freedom Media"
        };

        /// <summary>
        /// Short names of allVoices that are words, release tags ("ТВ-3" is the third TV season of an anime) or too rare
        /// to tell from one, and phrases that are a voice type, not a studio.
        /// </summary>
        static readonly HashSet<string> voiceDropped = new HashSet<string>
        {
            "Laci", "Vano", "Oni", "Jade", "Andy", "НСТ", "Че!", "MGM", "МИР", "Твин", "AOS",
            "D1", "Dice", "Gits", "jept", "KIHO", "Line", "MCA", "R5", "SGEV", "TB5", "Tori", "Troy", "Twix",
            "VHS", "ГКГ", "ИГМ", "Ирэн", "К9", "ТРК", "КiT", "ТВ-3", "ТВ-6"
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
            "Русский дубляж", "BD CEE", "Paramount Pictures",
            "Медіа Дім Рава", // a studio, not the channel Дім
            // actors listed after the studio, namesakes of translators, "со вставками В. Котова"
            "Александр Котов", "А. Котов", "Всеволод Кузнецов", "Михаил Хрусталев", "В. Котова",
            // channels and studios named by a word or a tag
            "ТВ3", "TV3", "TB3", "ТВ6", "TV6", "AMC", "АМС", "K1", "К1", "Новий", "Novy", "NLO", "НЛО", "Enter", "Дім",
            "Нота", "Nota", "Марафон", "Marafon",
            // online cinemas: in a release title the source of the rip, like NF or AMZN
            "Кинопоиск", "Kinopoisk", "Kinopoisk HD", "KP HD", "КинопоискHD", "Кинопоиск HD", "IVI", "Okko", "Окко",
            "KION", "Кион", "Megogo", "Мегого", "MGG", "Freedom Media",
            // sources of the file, never a voice
            "Netflix", "iTunes"
        };

        /// <summary>
        /// Matched only as written: as words they are something else ("Deep", "Fox"), or short names of audio tracks
        /// ("LF", "HDr").
        /// </summary>
        static readonly HashSet<string> voiceExact = new HashSet<string>
        {
            "FOX", "JAM", "AMS", "DEEP", "ТеТ", "ТЕТ", "TET", "HDr", "TVS", "LF", "NS", "SRb", "СБ", "CPI", "OPT", "HTB",
            "HATE", "Баритон", "Bariton", "Baritone", "ICG", "SDI", "ETV", "CTC", "STS", "THT", "TNT", "RTR", "HTH",
            "K1", "К1", "Новий", "НОВИЙ", "Novy", "NOVY", "Enter", "Дім", "ДІМ", "ДіМ"
        };

        /// <summary>Names of voiceAliases that give the language, besides rusVoices and ukrVoices.</summary>
        static readonly string[] voiceRus =
        {
            "Мосфильм-Мастер", "MovieDalen", "WStudio", "RuDub", "Paragraph Media", "WinMedia", "Dragon Money Studio",
            "1win Studio", "заКАДРЫ", "DubLikTV", "Akimbo Production", "Продубляж", "Light Breeze", "Leff Sound",
            "Soundmasters", "Vox Records", "В. Береговых", "Honey&Haseena", "SC Produb", "Храм тысячи струн",
            "Kazoku Project", "Digi Media", "Head Pack Films", "Delta Dubbing", "SoulPro", "Alt Pro", "HATE Studio",
            "Баритон", "Марафон", "Voize", "Videofilm International", "ТВ6", "Нота", "Інтер-фільм",
            "И. Котова", "В. Белов", "А. Яковлев", "А. Медведев", "Ю. Медведев", "С. Козлов", "А. Морозов", "И. Королёва",
            "Картавый Марченко", "Неоклассика"
        };

        static readonly string[] voiceUkr =
        {
            "Робота Голосом", "Glass Moon", "Tretyakoff Production", "Dzuski", "15КЗ", "CloverDUB", "VRdub", "Kioto Anime",
            "Pie Post Production", "Sweet Sound Studio", "What About Production", "ГайдаМайк", "UAFlix", "MEGOGO Voice",
            "K1", "Cine+", "Enter-фільм", "Суспільне Культура", "Дім", "ICTV2", "НТН"
        };

        /// <summary>
        /// Names allVoices lacks and other spellings of one name: name shown -> its spellings. A name of allVoices listed
        /// here is shown under the name on the left. People are "И. Фамилия".
        /// </summary>
        static readonly Dictionary<string, string[]> voiceAliases = new Dictionary<string, string[]>
        {
            ["JAM"] = new[] { "JAM", "JAM Club", "JAMCLUB" },
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
            ["Ozz"] = new[] { "Ozz", "Ozz.tv", "Ozz TV", "OzzTV" },
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
            ["Колобок"] = new[] { "Колобок", "Kолобок", "Студия Колобок", "Kolobok" },
            ["Omskbird"] = new[] { "Omskbird", "Omskbird Records" },
            ["Kerob"] = new[] { "Kerob", "KerobTV" },
            ["Flarrow Films"] = new[] { "Flarrow Films", "FlarrowFilms" },
            ["Ю. Сербин"] = new[] { "Ю. Сербин", "Сербин", "Юрий Сербин", "Serbin", "Yu.Serbin", "Y.Serbin", "SRb" },
            ["М. Яроцкий"] = new[] { "М. Яроцкий", "Яроцкий", "Михаил Яроцкий", "Yarotsky", "M.Yarotsky", "Kyberpunk", "Kyberpynk" },
            ["М. Чадов"] = new[] { "М. Чадов", "Чадов", "Михаил Чадов", "Chadov", "M.Chadov" },
            ["Д. Есарев"] = new[] { "Д. Есарев", "Есарев", "Дмитрий Есарев", "Esarev", "Yesarev", "D.Esarev", "D.Yesarev" },
            ["А. Гаврилов"] = new[] { "А. Гаврилов", "Гаврилов", "Андрей Гаврилов", "Gavrilov", "A.Gavrilov" },
            ["Ю. Живов"] = new[] { "Ю. Живов", "Живов", "Юрий Живов", "Zhivov", "Yu.Zhivov", "Jivov", "Givov", "Jyvov" },
            ["Д. Пучков"] = new[] { "Д. Пучков", "Пучков", "Дмитрий Пучков", "Гоблин", "Goblin" },
            ["Кубик в Кубе"] = new[] { "Кубик в Кубе", "KvK", "Kubik-V-Kube", "Kubik V Kube", "Kubik3", "kubik&ko", "Кубик в Кубе & Ko" },
            ["LeDoyen"] = new[] { "LeDoyen", "Le Doyen" },
            ["А. Матвеев"] = new[] { "А. Матвеев", "Матвеев", "Matveev", "Doctor Joker", "Dr. Joker", "Doctor Jocker" },
            ["А. Дольский"] = new[] { "А. Дольский", "Дольский", "Андрей Дольский", "Dolsky", "Dolskiy", "A.Dolsky", "A.Dolskiy" },
            ["Пифагор"] = new[] { "Пифагор", "Pythagor", "Pifagor" },
            ["Переводман"] = new[] { "Переводман", "Perevodman" },
            ["П. Гланц"] = new[] { "П. Гланц", "Гланц", "Пётр Гланц", "Петр Гланц", "Glanz", "Glanc", "P.Glanc", "P.Glantz", "P.Glanz" },
            ["Л. Володарский"] = new[] { "Л. Володарский", "Володарский", "Леонид Володарский", "L.Volodarsky", "Volodarsky", "Volodarskiy", "Volodarskij" },
            ["С. Визгунов"] = new[] { "С. Визгунов", "Визгунов", "Сергей Визгунов", "Vizgunov", "Vizgynov" },
            ["В. Горчаков"] = new[] { "В. Горчаков", "Горчаков", "Василий Горчаков", "Gorchakov" },
            ["В. Дохалов"] = new[] { "В. Дохалов", "Дохалов", "Вартан Дохалов", "Dohalov", "Dokhalov" },
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
            ["А. Карповский"] = new[] { "А. Карповский", "Карповский", "Karpovsky", "Karpovskiy", "A.Karpovsky" },
            ["А. Алексеев"] = new[] { "А. Алексеев", "Алексеев", "Антон Алексеев", "Alekseev", "Anton Alekseev" },
            ["А. Багичев"] = new[] { "А. Багичев", "Багичев", "Bagichev" },
            ["В. Завгородний"] = new[] { "В. Завгородний", "Завгородний", "V.Zavgorodny" },
            ["П. Карцев"] = new[] { "П. Карцев", "Карцев", "P.Kartsev", "Kartsev", "Karcev" },
            ["А. Кашкин"] = new[] { "А. Кашкин", "Кашкин", "Александр Кашкин", "Первомайский", "Kashkin", "A.Kashkin" },
            ["А. Киреев"] = new[] { "А. Киреев", "Киреев" },
            ["С. Кузнецов"] = new[] { "С. Кузнецов", "Кузнецов", "Сергей Кузнецов", "Kuznecov" },
            ["В. Курдов"] = new[] { "В. Курдов", "Курдов" },
            ["М. Латышев"] = new[] { "М. Латышев", "Латышев", "Максим Латышев", "Latyshev", "Хрусталев", "Е. Хрусталёв", "Егор Хрусталёв" },
            ["А. Марченко"] = new[] { "А. Марченко", "Марченко", "A.Marchenko" },
            ["Д. Нурмухаметов"] = new[] { "Д. Нурмухаметов", "Нурмухаметов", "D. Nurmukhametov" },
            ["А. Попов"] = new[] { "А. Попов", "Попов", "Алексей Попов", "A.Popov", "Popov", "V.Popov" },
            ["Е. Рудой"] = new[] { "Е. Рудой", "Рудой" },
            ["В. Рукин"] = new[] { "В. Рукин", "Рукин" },
            ["В. Сонькин"] = new[] { "В. Сонькин", "Сонькин" },
            ["Д. Строев"] = new[] { "Д. Строев", "Строев" },
            ["А. Тимофеев"] = new[] { "А. Тимофеев", "Тимофеев" },
            ["К. Филонов"] = new[] { "К. Филонов", "Филонов" },
            ["Интер Фильм"] = new[] { "Интер Фильм", "Интерфильм", "INTERFILM" },
            ["Позитив-Мультимедиа"] = new[] { "Позитив-Мультимедиа", "Позитив", "Позитив Мультимедиа", "Positive Multimedia", "Pozitiv Multimedia", "Pozitiv" },
            ["НТВ+"] = new[] { "НТВ+", "НТВ Плюс", "NTV+" },
            ["НТВ"] = new[] { "НТВ", "HTB", "NTV" },
            ["НТВ-Профит"] = new[] { "НТВ-Профит", "НТВ Профит" },
            ["ОРТ"] = new[] { "ОРТ", "ORT", "OPT" },
            ["Киномания"] = new[] { "Киномания", "Kinomania", "Kinomaniya", "Kinomanija", "Kinomanya" },
            ["Амальгама"] = new[] { "Амальгама", "Amalgama" },
            ["Невафильм"] = new[] { "Невафильм", "Nevafilm" },
            ["Кураж-Бамбей"] = new[] { "Кураж-Бамбей", "Kuraj-Bambey", "Kurazh-Bambey", "Kuraj Bambey" },
            ["Кинопоиск"] = new[] { "Кинопоиск", "Kinopoisk", "Kinopoisk HD", "KP HD", "КинопоискHD", "Кинопоиск HD" },
            ["Logos"] = new[] { "Logos", "Логос" },
            ["Русский Бестселлер"] = new[] { "Русский Бестселлер", "Бестселлер" },
            ["Tycoon"] = new[] { "Tycoon Studio", "Tycoon-Studio", "Тайкун" },
            ["Карусель"] = new[] { "Karusel" },
            ["Домашний"] = new[] { "Domashniy" },
            ["Дубляжная"] = new[] { "Дубляжная", "ТО Дубляжная", "TO Dublyazhnaya", "TO Dublyajnaya" },
            ["HDRezka"] = new[] { "HDRezka", "HDRezka Studio", "HDr" },
            ["TVShows"] = new[] { "TVShows", "TVS", "TVShow" },
            ["LostFilm"] = new[] { "LostFilm", "LostFilmTV", "Lost Film", "LF" },
            ["NewStudio"] = new[] { "NewStudio", "NS" },
            ["Студийная Банда"] = new[] { "Студийная Банда", "Studio Band", "StudioBand", "СБ" },
            ["CPI Films"] = new[] { "CPI Films", "CPI", "СиПиАй Филмз" },
            ["НЛО-TV"] = new[] { "НЛО-TV", "NLO TV", "NLO.TV", "НЛО-ТБ", "NLO", "НЛО" },
            ["Mallorn Studio"] = new[] { "Mallorn Studio", "Mallorn" },
            ["Україна"] = new[] { "Україна", "Украина", "Ukraina" },
            ["Інтер"] = new[] { "Інтер", "інтер", "IНТЕР", "Inter", "Интер" },
            ["ТЕТ"] = new[] { "ТЕТ", "ТеТ", "TET" },
            ["SDI Media"] = new[] { "SDI Media", "Iyuno-SDI Group", "Iyuno", "Iyuno Russia", "SDI" },
            ["СТС"] = new[] { "СТС", "CTC", "STS" },
            ["ТНТ"] = new[] { "ТНТ", "THT", "TNT" },
            ["РТР"] = new[] { "РТР", "RTR" },
            ["НТН"] = new[] { "НТН", "HTH", "NTN" },
            ["ETV+"] = new[] { "ETV+", "ETV" },
            ["Прайд Продакшн"] = new[] { "Прайд Продакшн", "Pride Production" },
            ["Гуртом"] = new[] { "Гуртом", "Hurtom", "Hurtom.com" },
            ["Велес"] = new[] { "Велес", "Veles" },
            ["Кипарис"] = new[] { "Кипарис", "Kiparis" },
            ["ViruseProject"] = new[] { "ViruseProject", "Viruse Project", "VirusProject" },
            ["Кириллица"] = new[] { "Кириллица", "Cyrillica", "Kirillitsa" },
            ["АрхиАзия"] = new[] { "АрхиАзия", "АрхиAsia" },
            ["CrazyCatStudio"] = new[] { "CrazyCatStudio", "Crazy Cat Studio", "Crazy Cat" },
            ["LE-Production"] = new[] { "LE-Production", "LE-Prod" },
            ["Pazl Voice"] = new[] { "Pazl Voice", "PazlVoice" },
            ["Варус-Видео"] = new[] { "Варус-Видео", "Varus Video" },
            ["Премьер Видео"] = new[] { "Премьер Видео", "Премьер Видеофильм", "Premier Videofilm" },
            ["Видеопродакшн"] = new[] { "Видеопродакшн", "Видео Продакшн" },
            ["BaibaKo"] = new[] { "BaibaKo", "BaibaKoTV" },
            ["Amber"] = new[] { "Amber", "Amber Studio", "Amber Studios" },
            ["Инис"] = new[] { "Инис", "Inis", "Студия Инис" },
            ["ICG"] = new[] { "ICG", "ICGTV" },
            ["CPIG"] = new[] { "CPIG", "Central Production International Group" },
            ["Е. Гранкин"] = new[] { "Е. Гранкин", "Гранкин", "Евгений Гранкин" },
            ["Н. Антонов"] = new[] { "Н. Антонов", "Антонов", "Николай Антонов", "Антонов Николай" },
            ["И. Клушин"] = new[] { "И. Клушин", "Клушин", "Игорь Клушин" },
            ["Махонько"] = new[] { "Махонько", "Mahonko" },
            // studios
            ["Мосфильм-Мастер"] = new[] { "Мосфильм-Мастер", "Mosfilm-Master" },
            ["MovieDalen"] = new[] { "MovieDalen", "Movie Dalen" },
            ["WStudio"] = new[] { "WStudio" },
            ["RuDub"] = new[] { "RuDub" },
            ["Paragraph Media"] = new[] { "Paragraph Media" },
            ["WinMedia"] = new[] { "WinMedia", "Winmedia Studio" },
            ["Dragon Money Studio"] = new[] { "Dragon Money Studio", "Dragon Money", "Драгон Мани Студио" },
            ["1win Studio"] = new[] { "1win Studio", "1WinStudio", "1win" },
            ["заКАДРЫ"] = new[] { "заКАДРЫ", "zaKADRY" },
            ["Akimbo Production"] = new[] { "Akimbo Production" },
            ["Продубляж"] = new[] { "Продубляж" },
            ["DubLikTV"] = new[] { "DubLikTV", "DubLik TV", "ДубликТВ" },
            ["Light Breeze"] = new[] { "Light Breeze", "Легкий Ветерок" },
            ["Leff Sound"] = new[] { "Leff Sound", "LeffSound" },
            ["Soundmasters"] = new[] { "Soundmasters" },
            ["Vox Records"] = new[] { "Vox Records", "Вокс Рекордс" },
            ["Honey&Haseena"] = new[] { "Honey&Haseena" },
            ["SC Produb"] = new[] { "SC Produb" },
            ["Храм тысячи струн"] = new[] { "Храм тысячи струн" },
            ["Kazoku Project"] = new[] { "Kazoku Project", "Kazoku" },
            ["Digi Media"] = new[] { "Digi Media" },
            ["TV3 Group"] = new[] { "TV3 Group" },
            ["Head Pack Films"] = new[] { "Head Pack Films" },
            ["Delta Dubbing"] = new[] { "Delta Dubbing", "Дельта Даббинг" },
            ["SoulPro"] = new[] { "SoulPro" },
            ["Alt Pro"] = new[] { "Alt Pro" },
            ["HATE Studio"] = new[] { "HATE Studio", "HATE" },
            ["Баритон"] = new[] { "Баритон", "Bariton", "Baritone" },
            ["Марафон"] = new[] { "Марафон", "Marafon", "Студия Марафон" },
            ["Нота"] = new[] { "Нота", "Nota", "Студия Нота" },
            ["Voize"] = new[] { "Voize" },
            ["Videofilm International"] = new[] { "Videofilm International", "Videofilm Int.", "Videofilm Ltd.", "Видеофильм Интернешнл" },
            ["В. Береговых"] = new[] { "В. Береговых", "Береговых", "Виктор Береговых" },
            ["Syncmer"] = new[] { "Syncmer" },
            ["Sunnysiders"] = new[] { "Sunnysiders", "Sunnysiders Audiovisual" },
            ["Cinema Sound Production"] = new[] { "Cinema Sound Production", "Cinema Sound UA Production", "Cinema Sound UA", "Cinema Sound" },
            ["Tretyakoff Production"] = new[] { "Tretyakoff Production" },
            ["GoLTFilm"] = new[] { "GoLTFilm" },
            ["Робота Голосом"] = new[] { "Робота Голосом", "Robota Holosom" },
            ["Glass Moon"] = new[] { "Glass Moon", "Gwean & Maslinka" },
            ["Dzuski"] = new[] { "Dzuski", "Dzuski.com" },
            ["15КЗ"] = new[] { "15КЗ", "15К3", "15K3", "15KЗ" },
            ["CloverDUB"] = new[] { "CloverDUB" },
            ["VRdub"] = new[] { "VRdub" },
            ["Kioto Anime"] = new[] { "Kioto Anime", "Кіото аніме" },
            ["Pie Post Production"] = new[] { "Pie Post Production" },
            ["Sweet Sound Studio"] = new[] { "Sweet Sound Studio", "Sweet Sound" },
            ["What About Production"] = new[] { "What About Production" },
            ["ГайдаМайк"] = new[] { "ГайдаМайк" },
            ["UAFlix"] = new[] { "UAFlix", "ЮАФЛІКС" },
            ["MEGOGO Voice"] = new[] { "MEGOGO Voice", "MGG Voice" },
            ["Інтер-фільм"] = new[] { "Інтер-фільм", "ІнтерФільм", "Iнтер-фiльм", "Inter-Film" },
            ["Cinema Tone Production"] = new[] { "Cinema Tone Production", "Cinema Tone", "CinemaTone" },
            // one studio or person under two names
            ["Неоклассика"] = new[] { "Неоклассика", "Neoclassica" },
            ["Лазер Видео"] = new[] { "Лазер Видео", "Lazer Video", "LazerVideo", "Laser Video", "LaserVideo" },
            ["AB-Video"] = new[] { "AB-Video", "Эй Би Видео" },
            ["Хихикающий доктор"] = new[] { "Хихикающий доктор", "Хихидок", "Xixidok", "Xixidoktor" },
            ["Elrom"] = new[] { "Elrom", "Ульпаней Эльром", "st.Elrom", "Elrom Studios", "студия Elrom", "Эльром" },
            ["Lizard Cinema Trade"] = new[] { "Lizard Cinema Trade", "Lizard Cinema", "Лизард", "Lizard" },
            ["диктор CDV"] = new[] { "диктор CDV", "Диктор компании C.D.V.", "Dictor CDV", "Diktor CDV", "Matros CDV" },
            ["CDV"] = new[] { "CDV", "C.D.V." },
            ["DniproFilm"] = new[] { "DniproFilm", "Дніпрофільм", "Днiпрофiльм" },
            ["АРК-ТВ"] = new[] { "АРК-ТВ", "Арк-ТВ", "АРК-ТВ Studio", "Студия АРК-ТВ" },
            ["Райдо"] = new[] { "Райдо", "Студия Райдо" },
            ["Anubis"] = new[] { "Anubis", "Анубис" },
            ["М. Логинофф"] = new[] { "М. Логинофф", "Логинофф", "Максим Логинофф", "Loginoff" },
            ["Р. Янкелевич"] = new[] { "Р. Янкелевич", "Янкелевич", "Роман Янкелевич", "Янкилевич", "Yankelevich", "R. Yankelevich" },
            ["Н. Золотухин"] = new[] { "Н. Золотухин", "Золотухин", "Николай Золотухин", "Уновец" },
            ["С. Дьяков"] = new[] { "С. Дьяков", "Дьяков", "Сергей Дьяков" },
            ["Е. Солодухин"] = new[] { "Е. Солодухин", "Солодухин", "Евгений Солодухин", "Solod" },
            ["А. Агапов"] = new[] { "А. Агапов", "Агапов", "Антон Агапов", "datynet" },
            ["А. Герусов"] = new[] { "А. Герусов", "Герусов", "Александр Герусов", "Gerusov", "Oneinchnales" },
            ["П. Морозов"] = new[] { "П. Морозов", "Павел Морозов", "PashaUp" },
            ["А. Морозов"] = new[] { "А. Морозов", "Александр Морозов" },
            // people: the initial, namesakes told apart by the first name; a bare namesake surname stays as it is
            ["В. Котов"] = new[] { "В. Котов", "Котов", "Вячеслав Котов", "V.Kotov", "Kotov", "mupoxa" },
            ["И. Котова"] = new[] { "И. Котова", "Ирина Котова" },
            ["М. Иванов"] = new[] { "М. Иванов", "Иванов", "Михаил Иванов", "Ivanov", "M.Ivanov" },
            ["П. Санаев"] = new[] { "П. Санаев", "Санаев", "Павел Санаев", "Sanaev" },
            ["В. Королёв"] = new[] { "В. Королёв", "Королёв", "Королев", "Владимир Королёв", "Korolev", "V.Korolev" },
            ["И. Королёва"] = new[] { "И. Королёва", "Инна Королёва", "Koroleva" },
            ["В. Вихров"] = new[] { "В. Вихров", "Вихров", "Владимир Вихров" },
            ["А. Клюквин"] = new[] { "А. Клюквин", "Клюквин", "Александр Клюквин" },
            ["Картавый Марченко"] = new[] { "Картавый Марченко" },
            ["В. Белов"] = new[] { "В. Белов", "Вадим Белов", "Wade White", "Editbox" },
            ["С. Белов"] = new[] { "С. Белов", "Сергей Белов", "Зереницын", "Сергей Зереницын", "Белов-Зереницын" },
            ["А. Яковлев"] = new[] { "А. Яковлев", "Алексей Яковлев", "Sephiroth" },
            ["В. Яковлев"] = new[] { "В. Яковлев", "Владимир Яковлев", "Самарский", "Борис Самарский", "Самаритянин", "Б. Федоров", "Борис Фёдоров" },
            ["А. Медведев"] = new[] { "А. Медведев", "Алексей Медведев" },
            ["Ю. Медведев"] = new[] { "Ю. Медведев", "Юрий Медведев" },
            ["С. Козлов"] = new[] { "С. Козлов", "Сергей Козлов" },
            ["В. Козлов"] = new[] { "В. Козлов", "Владимир Козлов", "Петербуржец" },
            // TV channels and online cinemas
            ["ТВ3"] = new[] { "ТВ3", "TV3", "TB3", "ТВ3 Россия" },
            ["ТВ6"] = new[] { "ТВ6", "TV6" },
            ["AMC"] = new[] { "AMC", "АМС" },
            ["K1"] = new[] { "K1", "К1" },
            ["Cine+"] = new[] { "Cine+" },
            ["Новий Канал"] = new[] { "Новий Канал", "Новый канал", "Novy Kanal", "Новий", "НОВИЙ", "Novy", "NOVY" },
            ["Enter-фільм"] = new[] { "Enter-фільм", "Enter-film", "Enter" },
            ["Суспільне Культура"] = new[] { "Суспільне Культура" },
            ["Дім"] = new[] { "Дім", "ДІМ", "ДіМ" },
            ["ICTV2"] = new[] { "ICTV2" },
            ["Okko"] = new[] { "Okko", "Окко" },
            ["KION"] = new[] { "KION", "Кион" },
            ["Megogo"] = new[] { "Megogo", "Мегого", "MGG" },
            ["Freedom Media"] = new[] { "Freedom Media" },
        };
    }
}
