using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Engine.CORE;
using JacRed.Models;
using JacRed.Models.Details;
using Newtonsoft.Json;

namespace JacRed.Engine
{
    public partial class FileDB : IDisposable
    {
        #region FileDB
        string fdbkey;

        public bool savechanges = false;

        FileDB(string key)
        {
            fdbkey = key;
            string fdbpath = pathDb(key);

            if (File.Exists(fdbpath))
                Database = JsonStream.Read<Dictionary<string, TorrentDetails>>(fdbpath) ?? new Dictionary<string, TorrentDetails>();
        }

        public Dictionary<string, TorrentDetails> Database = new Dictionary<string, TorrentDetails>();
        #endregion

        #region AddOrUpdate
        public void AddOrUpdate(TorrentBaseDetails torrent)
        {
            if (Database.TryGetValue(torrent.url, out TorrentDetails t))
            {
                bool updateFull = false;

                void upt(bool uptfull = false, bool updatetime = true)
                {
                    savechanges = true;

                    if (updatetime)
                        t.updateTime = DateTime.UtcNow;

                    if (uptfull)
                        updateFull = true;
                }

                #region types
                if (torrent.types != null)
                {
                    if (t.types == null)
                    {
                        t.types = torrent.types;
                        upt(true);
                    }
                    else
                    {
                        foreach (string type in torrent.types)
                        {
                            if (type != null && !t.types.Contains(type))
                                upt(true);
                        }

                        t.types = torrent.types;
                    }
                }
                #endregion

                if (torrent.trackerName != t.trackerName)
                {
                    t.trackerName = torrent.trackerName;
                    upt(true);
                }

                if (torrent.title != t.title)
                {
                    t.title = torrent.title;
                    upt(true);
                }

                if (!string.IsNullOrWhiteSpace(torrent.magnet) && torrent.magnet != t.magnet)
                {
                    t.ffprobe_tryingdata = 0;
                    t.magnet = torrent.magnet;
                    upt();
                }

                if (torrent.sid != t.sid)
                {
                    t.sid = torrent.sid;
                    upt(updatetime: false);
                }

                if (torrent.pir != t.pir)
                {
                    t.pir = torrent.pir;
                    upt(updatetime: false);
                }

                if (!string.IsNullOrWhiteSpace(torrent.sizeName) && torrent.sizeName != t.sizeName)
                {
                    t.sizeName = torrent.sizeName;
                    upt(true);
                }

                if (!string.IsNullOrWhiteSpace(torrent.name) && torrent.name != t.name)
                {
                    t.name = torrent.name;
                    t._sn = StringConvert.SearchName(t.name);
                    upt();
                }
                else if (string.IsNullOrWhiteSpace(t.name) && !string.IsNullOrWhiteSpace(torrent.title))
                {
                    t.name = torrent.title;
                    t._sn = StringConvert.SearchName(t.name);
                    upt();
                }

                if (!string.IsNullOrWhiteSpace(torrent.originalname) && torrent.originalname != t.originalname)
                {
                    t.originalname = torrent.originalname;
                    t._so = StringConvert.SearchName(t.originalname);
                    upt();
                }
                else if (string.IsNullOrWhiteSpace(t.originalname) && !string.IsNullOrWhiteSpace(torrent.title))
                {
                    t.originalname = torrent.title;
                    t._so = StringConvert.SearchName(t.originalname);
                    upt();
                }

                if (torrent.relased > 0 && torrent.relased != t.relased)
                {
                    t.relased = torrent.relased;
                    upt();
                }

                if (torrent.ffprobe != null && t.ffprobe == null)
                {
                    t.ffprobe = torrent.ffprobe;
                    upt();
                }

                if (updateFull)
                    updateFullDetails(t);

                else if (AppInit.conf.logFdb || AppInit.conf.log)
                    AppendFdbLog(torrent, t);

                t.checkTime = DateTime.Now;
                AddOrUpdateMasterDb(t);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(torrent.magnet) || torrent.types == null || torrent.types.Length == 0)
                    return;

                var name = torrent.name ?? torrent.title ?? "";
                var originalname = torrent.originalname ?? torrent.title ?? "";
                t = new TorrentDetails()
                {
                    url = torrent.url,
                    types = torrent.types,
                    trackerName = torrent.trackerName,
                    createTime = torrent.createTime,
                    updateTime = torrent.updateTime,
                    title = torrent.title,
                    name = name,
                    originalname = originalname,
                    pir = torrent.pir,
                    sid = torrent.sid,
                    relased = torrent.relased,
                    sizeName = torrent.sizeName,
                    magnet = torrent.magnet,
                    ffprobe = torrent.ffprobe
                };
                t._sn = StringConvert.SearchName(t.name);
                t._so = StringConvert.SearchName(t.originalname);

                savechanges = true;
                updateFullDetails(t);
                Database.TryAdd(t.url, t);
                AddOrUpdateMasterDb(t);
            }
        }
        #endregion

        #region FdbLog
        static readonly string FdbLogDir = "Data/log";
        const string FdbLogPrefix = "fdb.";

        static void AppendFdbLog(TorrentBaseDetails torrent, TorrentDetails t)
        {
            try
            {
                if (!Directory.Exists(FdbLogDir))
                    Directory.CreateDirectory(FdbLogDir);

                int retentionDays = AppInit.conf?.logFdbRetentionDays ?? 0;
                if (retentionDays > 0)
                {
                    var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
                    foreach (var path in Directory.EnumerateFiles(FdbLogDir, FdbLogPrefix + "*.log"))
                    {
                        string name = Path.GetFileNameWithoutExtension(path);
                        if (name.Length > FdbLogPrefix.Length && DateTime.TryParseExact(name.Substring(FdbLogPrefix.Length), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate) && fileDate < cutoff)
                            try { File.Delete(path); } catch { }
                    }
                }

                string logPath = Path.Combine(FdbLogDir, FdbLogPrefix + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log");
                string jsonLine = JsonConvert.SerializeObject(new List<TorrentBaseDetails>() { torrent, t }, Formatting.None) + "\n";
                File.AppendAllText(logPath, jsonLine);

                PurgeFdbLogBySizeAndCount();
            }
            catch { }
        }

        static void PurgeFdbLogBySizeAndCount()
        {
            int maxSizeMb = AppInit.conf?.logFdbMaxSizeMb ?? 0;
            int maxFiles = AppInit.conf?.logFdbMaxFiles ?? 0;
            if (maxSizeMb <= 0 && maxFiles <= 0)
                return;
            try
            {
                long maxBytes = maxSizeMb > 0 ? (long)maxSizeMb * 1024 * 1024 : long.MaxValue;
                var list = new List<(string path, long length, DateTime date)>();
                foreach (var path in Directory.EnumerateFiles(FdbLogDir, FdbLogPrefix + "*.log"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.Length <= FdbLogPrefix.Length || !DateTime.TryParseExact(name.Substring(FdbLogPrefix.Length), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate))
                        continue;
                    long len = 0;
                    try { len = new FileInfo(path).Length; } catch { }
                    list.Add((path, len, fileDate));
                }
                list.Sort((a, b) => a.date.CompareTo(b.date));
                long total = list.Sum(x => x.length);
                int count = list.Count;
                foreach (var item in list)
                {
                    if (total <= maxBytes && count <= maxFiles)
                        break;
                    try
                    {
                        File.Delete(item.path);
                        total -= item.length;
                        count--;
                    }
                    catch { }
                }
            }
            catch { }
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            if (Database.Count > 0 && savechanges)
                JsonStream.Write(pathDb(fdbkey), Database);

            if (openWriteTask.TryGetValue(fdbkey, out WriteTaskModel val))
            {
                val.openconnection -= 1;
                if (0 >= val.openconnection)
                {
                    if (!AppInit.conf.evercache.enable || (AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour > 0))
                        openWriteTask.TryRemove(fdbkey, out _);
                }
            }
        }
        #endregion


        #region updateFullDetails
        public static void updateFullDetails(TorrentDetails t)
        {
            #region getSizeInfo
            long getSizeInfo(string sizeName)
            {
                if (string.IsNullOrWhiteSpace(sizeName))
                    return 0;

                try
                {
                    double size = 0.1;
                    var gsize = Regex.Match(sizeName, "([0-9\\.,]+) (Mb|МБ|GB|ГБ|TB|ТБ)", RegexOptions.IgnoreCase).Groups;
                    if (!string.IsNullOrWhiteSpace(gsize[2].Value))
                    {
                        if (double.TryParse(gsize[1].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out size) && size != 0)
                        {
                            if (gsize[2].Value.ToLower() is "gb" or "гб")
                                size *= 1024;

                            if (gsize[2].Value.ToLower() is "tb" or "тб")
                                size *= 1048576;

                            return (long)(size * 1048576);
                        }
                    }
                }
                catch { }

                return 0;
            }
            #endregion

            t.size = getSizeInfo(t.sizeName);

            #region quality
            t.quality = 480;

            if (t.quality == 480)
            {
                if (t.title.Contains("720p"))
                {
                    t.quality = 720;
                }
                else if (t.title.Contains("1080p"))
                {
                    t.quality = 1080;
                }
                else if (Regex.IsMatch(t.title.ToLower(), "(4k|uhd)( |\\]|,|$)") || t.title.Contains("2160p"))
                {
                    // Вышел после 2000г
                    // Размер файла выше 10GB
                    // Есть пометка о 4K
                    t.quality = 2160;
                }
            }
            #endregion

            string titlelower = t.title.ToLower();

            #region videotype
            t.videotype = "sdr";
            if (Regex.IsMatch(titlelower, "(\\[|,| )hdr(10| |\\]|,|$)") || Regex.IsMatch(titlelower, "(10-bit|10 bit|10-бит|10 бит|hdr10)"))
            {
                if (!Regex.IsMatch(titlelower, "(\\[|,| )sdr( |\\]|,|$)"))
                    t.videotype = "hdr";
            }
            #endregion

            #region voice
            t.voices = new HashSet<string>();

            if (t.trackerName == "lostfilm")
            {
                t.voices.Add("LostFilm");
            }
            else if (t.trackerName == "hdrezka")
            {
                t.voices.Add("HDRezka");
            }

            if (Regex.IsMatch(titlelower, "( |x)(d|dub|дб|дуб|дубляж)(,| )"))
                t.voices.Add("Дубляж");

            foreach (string v in allVoices)
            {
                try
                {
                    if (v.Length > 4 && titlelower.Contains(v.ToLower()))
                        t.voices.Add(v);
                }
                catch { }
            }

            var streams = TracksDB.Get(t.magnet, t.types);
            if (streams != null)
            {
                foreach (var s in streams)
                {
                    if (string.IsNullOrEmpty(s.tags?.title))
                        continue;

                    if (s.codec_type != "audio")
                        continue;

                    foreach (string v in allVoices)
                    {
                        try
                        {
                            if (v.Length > 4 && s.tags.title.ToLower().Contains(v.ToLower()))
                                t.voices.Add(v);

                            if (Regex.IsMatch(s.tags.title.ToLower(), "( |x)(d|dub|дб|дуб|дубляж)(,| )"))
                                t.voices.Add("Дубляж");
                        }
                        catch { }
                    }
                }
            }
            #endregion

            #region languages
            t.languages = new HashSet<string>();

            if (titlelower.Contains("ukr") || titlelower.Contains("українськ") || titlelower.Contains("украинск") || t.trackerName == "toloka")
                t.languages.Add("ukr");

            if (t.trackerName == "lostfilm")
                t.languages.Add("rus");

            if (!t.languages.Contains("ukr"))
            {
                foreach (string v in ukrVoices)
                {
                    if (t.voices.Contains(v))
                    {
                        t.languages.Add("ukr");
                        break;
                    }
                }
            }

            if (!t.languages.Contains("rus"))
            {
                foreach (string v in rusVoices)
                {
                    if (t.voices.Contains(v))
                    {
                        t.languages.Add("rus");
                        break;
                    }
                }
            }
            #endregion

            #region seasons
            t.seasons = new HashSet<int>();

            if (t.types != null)
            {
                try
                {
                    if (t.types.Contains("serial") || t.types.Contains("multserial") || t.types.Contains("docuserial") || t.types.Contains("tvshow") || t.types.Contains("anime"))
                    {
                        if (Regex.IsMatch(t.title, "([0-9]+(\\-[0-9]+)?x[0-9]+|сезон|s[0-9]+)", RegexOptions.IgnoreCase))
                        {
                            if (Regex.IsMatch(t.title, "([0-9]+\\-[0-9]+x[0-9]+|[0-9]+\\-[0-9]+ сезон|s[0-9]+\\-[0-9]+)", RegexOptions.IgnoreCase))
                            {
                                #region Несколько сезонов
                                int startSeason = 0, endSeason = 0;

                                if (Regex.IsMatch(t.title, "[0-9]+x[0-9]+", RegexOptions.IgnoreCase))
                                {
                                    var g = Regex.Match(t.title, "([0-9]+)\\-([0-9]+)x", RegexOptions.IgnoreCase).Groups;
                                    int.TryParse(g[1].Value, out startSeason);
                                    int.TryParse(g[2].Value, out endSeason);
                                }
                                else if (Regex.IsMatch(t.title, "[0-9]+ сезон", RegexOptions.IgnoreCase))
                                {
                                    var g = Regex.Match(t.title, "([0-9]+)\\-([0-9]+) сезон", RegexOptions.IgnoreCase).Groups;
                                    int.TryParse(g[1].Value, out startSeason);
                                    int.TryParse(g[2].Value, out endSeason);
                                }
                                else if (Regex.IsMatch(t.title, "s[0-9]+", RegexOptions.IgnoreCase))
                                {
                                    var g = Regex.Match(t.title, "s([0-9]+)\\-([0-9]+)", RegexOptions.IgnoreCase).Groups;
                                    int.TryParse(g[1].Value, out startSeason);
                                    int.TryParse(g[2].Value, out endSeason);
                                }

                                if (startSeason > 0 && endSeason > startSeason)
                                {
                                    for (int s = startSeason; s <= endSeason; s++)
                                        t.seasons.Add(s);
                                }
                                #endregion
                            }
                            if (Regex.IsMatch(t.title, "[0-9]+ сезон", RegexOptions.IgnoreCase))
                            {
                                #region Один сезон
                                if (Regex.IsMatch(t.title, "[0-9]+ сезон", RegexOptions.IgnoreCase))
                                {
                                    if (int.TryParse(Regex.Match(t.title, "([0-9]+) сезон", RegexOptions.IgnoreCase).Groups[1].Value, out int s) && s > 0)
                                        t.seasons.Add(s);
                                }
                                #endregion
                            }
                            else if (Regex.IsMatch(t.title, "сезон(ы|и)?:? [0-9]+\\-[0-9]+", RegexOptions.IgnoreCase))
                            {
                                #region Несколько сезонов
                                int startSeason = 0, endSeason = 0;

                                if (Regex.IsMatch(t.title, "сезон(ы|и)?:? [0-9]+", RegexOptions.IgnoreCase))
                                {
                                    var g = Regex.Match(t.title, "сезон(ы|и)?:? ([0-9]+)\\-([0-9]+)", RegexOptions.IgnoreCase).Groups;
                                    int.TryParse(g[2].Value, out startSeason);
                                    int.TryParse(g[3].Value, out endSeason);
                                }

                                if (startSeason > 0 && endSeason > startSeason)
                                {
                                    for (int s = startSeason; s <= endSeason; s++)
                                        t.seasons.Add(s);
                                }
                                #endregion
                            }
                            else
                            {
                                #region Один сезон
                                if (Regex.IsMatch(t.title, "[0-9]+x[0-9]+", RegexOptions.IgnoreCase))
                                {
                                    if (int.TryParse(Regex.Match(t.title, "([0-9]+)x", RegexOptions.IgnoreCase).Groups[1].Value, out int s) && s > 0)
                                        t.seasons.Add(s);
                                }
                                else if (Regex.IsMatch(t.title, "сезон(ы|и)?:? [0-9]+", RegexOptions.IgnoreCase))
                                {
                                    if (int.TryParse(Regex.Match(t.title, "сезон(ы|и)?:? ([0-9]+)", RegexOptions.IgnoreCase).Groups[2].Value, out int s) && s > 0)
                                        t.seasons.Add(s);
                                }
                                else if (Regex.IsMatch(t.title, "s[0-9]+", RegexOptions.IgnoreCase))
                                {
                                    if (int.TryParse(Regex.Match(t.title, "s([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value, out int s) && s > 0)
                                        t.seasons.Add(s);
                                }
                                #endregion
                            }
                        }
                    }
                }
                catch { }
            }
            #endregion
        }
        #endregion

        static HashSet<string> allVoices = new HashSet<string> { "Ozz", "Laci", "Kerob", "LE-Production", "Parovoz Production", "Paradox", "Omskbird", "LostFilm", "Причудики", "BaibaKo", "NewStudio", "AlexFilm", "FocusStudio", "Gears Media", "Jaskier", "ViruseProject", "Кубик в Кубе", "IdeaFilm", "Sunshine Studio", "Ozz.tv", "Hamster Studio", "Сербин", "To4ka", "Кравец", "Victory-Films", "SNK-TV", "GladiolusTV", "Jetvis Studio", "ApofysTeam", "ColdFilm", "Agatha Studdio", "KinoView", "Jimmy J.", "Shadow Dub Project", "Amedia", "Red Media", "Selena International", "Гоблин", "Universal Russia", "Kiitos", "Paramount Comedy", "Кураж-Бамбей", "Студия Пиратского Дубляжа", "Чадов", "Карповский", "RecentFilms", "Первый канал", "Alternative Production", "NEON Studio", "Колобок", "Дольский", "Синема УС", "Гаврилов", "Живов", "SDI Media", "Алексеев", "GreenРай Studio", "Михалев", "Есарев", "Визгунов", "Либергал", "Кузнецов", "Санаев", "ДТВ", "Дохалов", "Горчаков", "LevshaFilm", "CasStudio", "Володарский", "Шварко", "Карцев", "ETV+", "ВГТРК", "Gravi-TV", "1001cinema", "Zone Vision Studio", "Хихикающий доктор", "Murzilka", "turok1990", "FOX", "STEPonee", "Elrom", "HighHopes", "SoftBox", "NovaFilm", "Четыре в квадрате", "Greb&Creative", "MUZOBOZ", "ZM-Show", "Kerems13", "New Dream Media", "Игмар", "Котов", "DeadLine Studio", "РенТВ", "Андрей Питерский", "Fox Life", "Рыбин", "Trdlo.studio", "Studio Victory Аsia", "Ozeon", "НТВ", "CP Digital", "AniLibria", "Levelin", "FanStudio", "Cmert", "Интерфильм", "SunshineStudio", "Kulzvuk Studio", "Кашкин", "Вартан Дохалов", "Немахов", "Sedorelli", "СТС", "Яроцкий", "ICG", "ТВЦ", "Штейн", "AzOnFilm", "SorzTeam", "Гаевский", "Мудров", "Воробьев Сергей", "Студия Райдо", "DeeAFilm Studio", "zamez", "Иванов", "СВ-Дубль", "BadBajo", "Комедия ТВ", "Мастер Тэйп", "5-й канал СПб", "Гланц", "Ох! Студия", "СВ-Кадр", "2x2", "Котова", "Позитив", "RusFilm", "Назаров", "XDUB Dorama", "Реальный перевод", "Kansai", "Sound-Group", "Николай Дроздов", "ZEE TV", "MTV", "Сыендук", "GoldTeam", "Белов", "Dream Records", "Яковлев", "Vano", "SilverSnow", "Lord32x", "Filiza Studio", "Sony Sci-Fi", "Flux-Team", "NewStation", "DexterTV", "Good People", "AniDUB", "SHIZA Project", "AniLibria.TV", "StudioBand", "AniMedia", "Onibaku", "JWA Project", "MC Entertainment", "Oni", "Jade", "Ancord", "ANIvoice", "Nika Lenina", "Bars MacAdams", "JAM", "Anika", "Berial", "Kobayashi", "Cuba77", "RiZZ_fisher", "OSLIKt", "Lupin", "Ryc99", "Nazel & Freya", "Trina_D", "JeFerSon", "Vulpes Vulpes", "Hamster", "KinoGolos", "Fox Crime", "Денис Шадинский", "AniFilm", "Rain Death", "New Records", "Первый ТВЧ", "RG.Paravozik", "Profix Media", "Tycoon", "RealFake", "HDRezka", "Discovery", "Viasat History", "HiWayGrope", "GREEN TEA", "AlphaProject", "AnimeReactor", "Animegroup", "Shachiburi", "Persona99", "3df voice", "CactusTeam", "AniMaunt", "ShinkaDan", "ShowJet", "RAIM", "АрхиТеатр", "Project Web Mania", "ko136", "КураСгречей", "AMS", "СВ-Студия", "Храм Дорам ТВ", "TurkStar", "Медведев", "Рябов", "BukeDub", "FilmGate", "FilmsClub", "Sony Turbo", "AXN Sci-Fi", "DIVA Universal", "Курдов", "Неоклассика", "fiendover", "SomeWax", "Логинофф", "Cartoon Network", "Loginoff", "CrezaStudio", "Воротилин", "LakeFilms", "Andy", "XDUB Dorama + Колобок", "KosharaSerials", "Екатеринбург Арт", "Julia Prosenuk", "АРК-ТВ Studio", "Т.О Друзей", "Animedub", "Paramount Channel", "Кириллица", "AniPLague", "Видеосервис", "JoyStudio", "TVShows", "GostFilm", "West Video", "Формат AB", "Film Prestige", "SovetRomantica", "РуФилмс", "AveBrasil", "BTI Studios", "Пифагор", "Eurochannel", "Кармен Видео", "Кошкин", "Rainbow World", "Варус-Видео", "ClubFATE", "HiWay Grope", "Banyan Studio", "Mallorn Studio", "Asian Miracle Group", "Эй Би Видео", "AniStar", "Korean Craze", "Невафильм", "Hallmark", "Sony Channel", "East Dream", "Bonsai Studio", "Lucky Production", "Octopus", "TUMBLER Studio", "CrazyCatStudio", "Amber", "Train Studio", "Анастасия Гайдаржи", "Мадлен Дюваль", "Sound Film", "Cowabunga Studio", "Фильмэкспорт", "VO-Production", "Nickelodeon", "MixFilm", "Back Board Cinema", "Кирилл Сагач", "Stevie", "OnisFilms", "MaxMeister", "Syfy Universal", "Neo-Sound", "Муравский", "Рутилов", "Тимофеев", "Лагута", "Дьяконов", "Voice Project", "VoicePower", "StudioFilms", "Elysium", "BeniAffet", "Paul Bunyan", "CoralMedia", "Кондор", "ViP Premiere", "FireDub", "AveTurk", "Янкелевич", "Киреев", "Багичев", "Лексикон", "Нота", "Arisu", "Superbit", "AveDorama", "VideoBIZ", "Киномания", "DDV", "WestFilm", "Анастасия Гайдаржи + Андрей Юрченко", "VSI Moscow", "Horizon Studio", "Flarrow Films", "Amazing Dubbing", "Видеопродакшн", "VGM Studio", "FocusX", "CBS Drama", "Novamedia", "Дасевич", "Анатолий Гусев", "Twister", "Морозов", "NewComers", "kubik&ko", "DeMon", "Анатолий Ашмарин", "Inter Video", "Пронин", "AMC", "Велес", "Volume-6 Studio", "Хоррор Мэйкер", "Ghostface", "Sephiroth", "Акира", "Деваль Видео", "RussianGuy27", "neko64", "Shaman", "Franek Monk", "Ворон", "Andre1288", "GalVid", "Другое кино", "Студия NLS", "Sam2007", "HaseRiLLoPaW", "Севастьянов", "D.I.M.", "Марченко", "Журавлев", "Н-Кино", "Lazer Video", "SesDizi", "Рудой", "Товбин", "Сергей Дидок", "Хуан Рохас", "binjak", "Карусель", "Lizard Cinema", "Акцент", "Max Nabokov", "Barin101", "Васька Куролесов", "Фортуна-Фильм", "Amalgama", "AnyFilm", "Козлов", "Zoomvision Studio", "Urasiko", "VIP Serial HD", "НСТ", "Кинолюкс", "Завгородний", "AB-Video", "Universal Channel", "Wakanim", "SnowRecords", "С.Р.И", "Старый Бильбо", "Mystery Film", "Латышев", "Ващенко", "Лайко", "Сонотек", "Psychotronic", "Gremlin Creative Studio", "Нева-1", "Максим Жолобов", "Мобильное телевидение", "IVI", "DoubleRec", "Milvus", "RedDiamond Studio", "Astana TV", "Никитин", "КТК", "D2Lab", "Black Street Records", "Останкино", "TatamiFilm", "Видеобаза", "Crunchyroll", "RedRussian1337", "КонтентикOFF", "Creative Sound", "HelloMickey Production", "Пирамида", "CLS Media", "Сонькин", "Garsu Pasaulis", "Gold Cinema", "Че!", "Нарышкин", "Intra Communications", "Кипарис", "Королёв", "visanti-vasaer", "Готлиб", "диктор CDV", "Pazl Voice", "Прямостанов", "Zerzia", "MGM", "Дьяков", "Вольга", "Дубровин", "МИР", "Jetix", "RUSCICO", "Seoul Bay", "Филонов", "Махонько", "Строев", "Саня Белый", "Говинда Рага", "Ошурков", "Horror Maker", "Хлопушка", "Хрусталев", "Антонов Николай", "Золотухин", "АрхиАзия", "Попов", "Ultradox", "Мост-Видео", "Альтера Парс", "Огородников", "Твин", "Хабар", "AimaksaLTV", "ТНТ", "FDV", "The Kitchen Russia", "Ульпаней Эльром", "Видеоимпульс", "GoodTime Media", "Alezan", "True Dubbing Studio", "Интер", "Contentica", "Мельница", "ИДДК", "Инфо-фильм", "Мьюзик-трейд", "Кирдин | Stalk", "ДиоНиК", "Стасюк", "TV1000", "Тоникс Медиа", "Бессонов", "Бахурани", "NewDub", "Cinema Prestige", "Набиев", "ТВ3", "Малиновский Сергей", "Кенс Матвей", "Voiz", "Светла", "LDV", "Videogram", "Индия ТВ", "Герусов", "Элегия фильм", "Nastia", "Семыкина Юлия", "Электричка", "Штамп Дмитрий", "Пятница", "Oneinchnales", "Кинопремьера", "Бусов Глеб", "Emslie", "1+1", "100 ТВ", "1001 cinema", "2+2", "2х2", "4u2ges", "5 канал", "A. Lazarchuk", "AAA-Sound", "AdiSound", "ALEKS KV", "Amalgam", "AnimeSpace Team", "AniUA", "AniWayt", "Anything-group", "AOS", "Arasi project", "ARRU Workshop", "AuraFilm", "AvePremier", "Azazel", "BadCatStudio", "BBC Saint-Petersburg", "BD CEE", "Boльгa", "Brain Production", "BraveSound", "Bubble Dubbing Company", "Byako Records", "Cactus Team", "CDV", "CinemaSET GROUP", "CinemaTone", "CPIG", "D1", "datynet", "DeadLine", "DeadSno", "den904", "Description", "Dice", "DniproFilm", "DreamRecords", "DVD Classic", "Eladiel", "Elegia", "ELEKTRI4KA", "Epic Team", "eraserhead", "erogg", "Extrabit", "F-TRAIN", "Family Fan Edition", "Fox Russia", "FoxLife", "Foxlight", "Gala Voices", "Gemini", "General Film", "GetSmart", "Gezell Studio", "Gits", "GoodVideo", "Gramalant", "HamsterStudio", "hungry_inri", "ICTV", "IgVin &amp; Solncekleshka", "ImageArt", "INTERFILM", "Ivnet Cinema", "IНТЕР", "Jakob Bellmann", "Janetta", "jept", "Jetvis", "JimmyJ", "KIHO", "Kinomania", "Kолобок", "L0cDoG", "LeDoyen", "LeXiKC", "Liga HQ", "Line", "Lisitz", "Lizard Cinema Trade", "lord666", "Macross", "madrid", "Marclail", "MCA", "McElroy", "Mega-Anime", "Melodic Voice Studio", "metalrus", "MifSnaiper", "Mikail", "Milirina", "MiraiDub", "MOYGOLOS", "MrRose", "National Geographic", "NemFilm", "Neoclassica", "Nice-Media", "No-Future", "Oghra-Brown", "OpenDub", "Ozz TV", "PaDet", "Paramount Pictures", "PashaUp", "PCB Translate", "PiratVoice", "Postmodern", "Prolix", "QTV", "R5", "Radamant", "RainDeath", "RATTLEBOX", "Reanimedia", "Rebel Voice", "RedDog", "Renegade Team", "RG Paravozik", "RinGo", "RoxMarty", "Rumble", "Saint Sound", "SakuraNight", "Satkur", "Sawyer888", "Sci-Fi Russia", "Selena", "seqw0", "SGEV", "SHIZA", "Sky Voices", "SkyeFilmTV", "SmallFilm", "SOLDLUCK2", "Solod", "SpaceDust", "ssvss", "st.Elrom", "Suzaku", "sweet couple", "TB5", "TF-AniGroup", "The Mike Rec.", "Timecraft", "To4kaTV", "Tori", "Total DVD", "TrainStudio", "Troy", "TV 1000", "Twix", "VashMax2", "VendettA", "VHS", "VicTeam", "VictoryFilms", "Video-BIZ", "VIZ Media", "Voice Project Studio", "VulpesVulpes", "Wayland team", "WiaDUB", "WVoice", "XL Media", "XvidClub Studio", "Zendos", "Zone Studio", "Zone Vision", "Агапов", "Акопян", "Артемьев", "Васильев", "Васильцев", "Григорьев", "Клюквин", "Костюкевич", "Матвеев", "Мишин", "Савченко", "Смирнов", "Толстобров", "Чуев", "Шуваев", "ААА-sound", "АБыГДе", "Акалит", "Альянс", "Амальгама", "АМС", "АнВад", "Анубис", "Anubis", "Арк-ТВ", "Б. Федоров", "Бибиков", "Бигыч", "Бойков", "Абдулов", "Вихров", "Воронцов", "Данилов", "Рукин", "Варус Видео", "Ващенко С.", "Векшин", "Весельчак", "Витя <говорун>", "Войсовер", "Г. Либергал", "Г. Румянцев", "Гей Кино Гид", "ГКГ", "Глуховский", "Гризли", "Гундос", "Деньщиков", "Нурмухаметов", "Пучков", "Шадинский", "Штамп", "sf@irat", "Держиморда", "Домашний", "Е. Гаевский", "Е. Гранкин", "Е. Лурье", "Е. Рудой", "Е. Хрусталёв", "ЕА Синема", "Живаго", "Жучков", "З Ранку До Ночі", "Зебуро", "Зереницын", "И. Еремеев", "И. Клушин", "И. Сафронов", "И. Степанов", "ИГМ", "Имидж-Арт", "Инис", "Ирэн", "Ист-Вест", "К. Поздняков", "К. Филонов", "К9", "Карапетян", "Квадрат Малевича", "Килька", "Королев", "Л. Володарский", "Лазер Видео", "ЛанселаП", "Лапшин", "Ленфильм", "Леша Прапорщик", "Лизард", "Люсьена", "Заугаров", "Иванова и П. Пашут", "Максим Логинофф", "Малиновский", "Машинский", "Медиа-Комплекс", "Мика Бондарик", "Миняев", "Мительман", "Мост Видео", "Мосфильм", "Н. Антонов", "Н. Дроздов", "Н. Золотухин", "Н.Севастьянов seva1988", "Наталья Гурзо", "НЕВА 1", "НеЗупиняйПродакшн", "Несмертельное оружие", "НЛО-TV", "Новый диск", "Новый Дубляж", "НТН", "Оверлорд", "Омикрон", "Парадиз", "Пепелац", "Первый канал ОРТ", "Переводман", "Перец", "Петербургский дубляж", "Петербуржец", "Позитив-Мультимедиа", "Прайд Продакшн", "Премьер Видео", "Премьер Мультимедиа", "Р. Янкелевич", "Райдо", "Ракурс", "Россия", "РТР", "Русский дубляж", "Русский Репортаж", "Рыжий пес", "С. Визгунов", "С. Дьяков", "С. Казаков", "С. Кузнецов", "С. Кузьмичёв", "С. Лебедев", "С. Макашов", "С. Рябов", "С. Щегольков", "С.Р.И.", "Сolumbia Service", "Самарский", "СВ Студия", "Селена Интернешнл", "Синема Трейд", "Синта Рурони", "Синхрон", "Советский", "Сокуров", "Солодухин", "Союз Видео", "Союзмультфильм", "СПД - Сладкая парочка", "Студии Суверенного Лепрозория", "Студия <Стартрек>", "KOleso", "Студия Горького", "Студия Колобок", "Студия Трёх", "Гуртом", "Супербит", "Так Треба Продакшн", "ТВ XXI век", "ТВ СПб", "ТВ-3", "ТВ6", "ТВЧ 1", "ТО Друзей", "Толмачев", "Точка Zрения", "Трамвай-фильм", "ТРК", "Уолт Дисней Компани", "Хихидок", "Цікава ідея", "Швецов", "Ю. Живов", "Ю. Немахов", "Ю. Сербин", "Ю. Товбин", "Я. Беллманн", "RHS", "Red Head Sound", "Postmodern Postproduction", "MelodicVoiceStudio", "FanVoxUA", "UkraineFastDUB", "UFDUB", "CHAS.UA", "Струґачка", "StorieS man", "UATeam", "UkrDub", "UAVoice", "Три крапки", "Сокира", "FlameStudio", "HATOSHI", "SkiDub", "Sengoku", "AdrianZP", "Cikava Ideya", "КiT", "Inter", "NLO", "ТакТребаПродакшн", "Новий Канал", "BambooUA", "Тоніс", "UA-DUB", "ТеТ", "СТБ", "НЛО", "Колодій", "В одне рило", "інтер", "DubLiCat", "AAASound", "НеЗупиняйПродакшн", "Омікрон", "Omicron", "Omikron", "3 крапки", "Tak Treba Production", "TET", "ПлюсПлюс", "Дніпрофільм", "ArtymKo", "Cinemaker", "sweet.tv", "DreamCast" };

        static HashSet<string> rusVoices = new HashSet<string> { "LostFilm", "Горчаков", "Кириллица", "TVShows", "datynet", "Gears Media", "Ленфильм", "Пифагор", "Jaskier", "Сербин", "Ю. Сербин", "Superbit", "Гланц", "Королев", "Мосфильм", "Яроцкий", "Немахов", "Ю. Немахов", "Визгунов", "Премьер Мультимедиа", "СВ-Дубль", "Рябов", "Яковлев", "Велес", "Хлопушка", "Марченко", "Живов", "Либергал", "Г. Либергал", "Tycoon", "1001cinema", "1001 cinema", "FOX", "LakeFilms", "zamez", "SNK-TV", "С. Визгунов", "SDI Media", "Первый канал", "NewComers", "Гоблин", "Карусель", "Иванов", "Карповский", "Twister", "ДТВ", "ТВЦ", "НТВ", "Королёв", "Ю. Живов", "Видеосервис", "Санаев", "Варус Видео", "Кашкин", "Кубик в Кубе", "Варус-Видео", "BadBajo", "Flarrow Films", "Нева-1", "Пучков", "Pazl Voice", "Есарев", "Завгородний", "Латышев", "Red Head Sound", "Чадов", "Союз Видео", "Film Prestige", "Михалев", "Кравец", "GREEN TEA", "Позитив", "Позитив-Мультимедиа", "ТВ3", "Карцев", "CactusTeam", "Cactus Team", "D2Lab", "Vano", "Воротилин", "Супербит", "С. Рябов", "STEPonee", "DeadSno", "den904", "Back Board Cinema", "AlexFilm", "Рутилов", "Zone Vision", "Смирнов", "Янкелевич", "Колобок", "NewStation", "MUZOBOZ", "Алексеев", "NewStudio", "RusFilm", "2x2", "VO-Production", "Ivnet Cinema", "Володарский", "Дохалов", "Вартан Дохалов", "Медведев", "Amedia", "Novamedia", "TV1000", "TV 1000", "Мика Бондарик", "Amalgama", "Amalgam", "Дьяконов", "Игмар", "ИГМ", "ВГТРК", "Л. Володарский", "Гаевский", "Е. Гаевский", "Garsu Pasaulis", "Кузнецов", "Премьер Видео", "AB-Video", "CP Digital", "Селена Интернешнл", "Махонько", "GoodTime Media", "Рукин", "НСТ", "Филонов", "Деваль Видео", "Екатеринбург Арт", "DoubleRec", "Твин", "Синхрон", "Русский дубляж", "СТС", "ViruseProject", "ТВ-3", "Ворон", "АрхиАзия", "Светла", "Котов", "West Video", "IdeaFilm", "С.Р.И", "С.Р.И.", "Кипарис", "С. Кузнецов", "BTI Studios", "NovaFilm", "Horror Maker", "ТНТ", "Огородников", "Б. Федоров", "ICG", "Solod", "ColdFilm", "ViP Premiere", "CinemaTone", "FDV", "RussianGuy27", "Hamster", "AMS", "РТР", "Багичев", "JimmyJ", "Cinema Prestige", "RHS", "AniMaunt", "Штейн", "Амальгама", "Пирамида", "Н. Антонов", "Товбин", "Ю. Товбин", "Матвеев", "Советский", "Кармен Видео", "Paradox", "ZM-Show", "Saint Sound", "Попов", "GladiolusTV", "RUSCICO", "RealFake", "SesDizi", "Мишин", "Киреев", "Good People", "Мост-Видео", "Сонькин", "AlphaProject", "Останкино", "DDV", "Назаров", "Пронин", "FocusStudio", "Хрусталев", "SomeWax", "Строев", "Дасевич", "Лазер Видео", "К. Поздняков", "Весельчак", "Прямостанов", "Видеопродакшн", "Логинофф", "Максим Логинофф", "Козлов", "Ващенко", "СВ Студия", "ETV+", "диктор CDV", "CDV", "Кондор", "Мост Видео", "Gemini", "Lucky Production", "Дубровин", "ShowJet", "lord666", "Солодухин", "Gravi-TV", "Gramalant", "Акцент", "seqw0", "Profix Media", "АРК-ТВ", "Mallorn Studio", "Причудики", "Sawyer888", "Ист-Вест", "FanStudio", "CrazyCatStudio", "PashaUp", "Сонотек", "Синта Рурони", "Видеоимпульс", "Белов", "Сыендук", "Sony Turbo", "РенТВ", "Инис", "Воронцов", "Р. Янкелевич", "Zone Vision Studio", "Anubis", "Ошурков", "Asian Miracle Group", "Sephiroth", "Вихров", "Elrom", "Русский Репортаж", "SoftBox", "DIVA Universal", "Hallmark", "Другое кино", "SkyeFilmTV", "Е. Гранкин", "Levelin", "Omskbird", "Синема УС", "Герусов", "New Dream Media", "RAIM", "Кураж-Бамбей", "Фильмэкспорт", "Савченко", "Парадиз", "Севастьянов", "Васильев", "SHIZA", "Рудой", "Е. Рудой", "CBS Drama", "Толстобров", "Lord32x", "Bonsai Studio", "KosharaSerials", "Selena", "Дьяков", "HiWayGrope", "HiWay Grope", "BraveSound", "Синема Трейд", "CPIG", "Ирэн", "Котова", "Н-Кино", "Andy", "Лагута", "Райдо", "AniPLague", "AdiSound", "visanti-vasaer", "Держиморда", "GreenРай Studio", "ZEE TV", "VoicePower", "Хуан Рохас", "Фортуна-Фильм", "Войсовер", "Ozz.tv", "Векшин", "RATTLEBOX", "Ракурс", "Radamant", "RecentFilms", "Anika", "True Dubbing Studio", "Штамп", "BadCatStudio", "Kiitos", "VictoryFilms", "Кошкин", "SpaceDust", "DeMon", "sf@irat", "Данилов", "Переводман", "Nice-Media", "Никитин", "Акира", "Я. Беллманн", "С. Дьяков", "Новый диск", "ALEKS KV", "Videogram", "Морозов", "Хоррор Мэйкер", "CinemaSET GROUP", "Деньщиков", "Мобильное телевидение", "ИДДК", "Intra Communications", "Петербуржец", "Greb&Creative", "Стасюк", "СВ-Кадр", "Готлиб", "Тоникс Медиа", "fiendover", "RoxMarty", "Project Web Mania", "Золотухин", "madrid", "Нурмухаметов", "Lazer Video", "OnisFilms", "FilmGate", "JAM", "Григорьев", "Ульпаней Эльром", "Мудров", "Альянс", "Filiza Studio", "XDUB Dorama", "Жучков", "eraserhead", "Комедия ТВ", "Леша Прапорщик", "WestFilm", "Швецов", "Хихидок", "The Kitchen Russia", "Psychotronic", "ДиоНиК", "PiratVoice", "Штамп Дмитрий", "Red Media", "Саня Белый", "Мельница", "Бибиков", "Urasiko", "XL Media", "kubik&ko", "Jakob Bellmann", "Зереницын", "Мастер Тэйп", "Лизард", "Agatha Studdio", "MOYGOLOS", "Нарышкин", "Franek Monk", "Train Studio", "TrainStudio", "D.I.M.", "AniStar", "Клюквин", "Бойков", "Voiz", "Amber", "MrRose", "AniLibria", "RG.Paravozik", "Гей Кино Гид", "НЕВА 1", "Машинский", "Т.О Друзей", "Cmert", "Parovoz Production", "VideoBIZ", "Oneinchnales", "Васька Куролесов", "Живаго", "Lizard Cinema", "Lizard Cinema Trade", "Ghostface", "РуФилмс", "Kansai", "АнВад", "Liga HQ", "AniMedia", "Reanimedia", "LeXiKC", "Зебуро", "Артемьев", "Самарский", "Перец", "To4ka", "Лексикон", "McElroy", "Муравский", "Zerzia", "Первый канал ОРТ", "RedDiamond Studio", "Sky Voices", "Creative Sound", "Jetvis Studio", "Jetvis", "CLS Media", "СВ-Студия", "Васильцев", "MGM", "L0cDoG", "RedRussian1337", "Black Street Records", "Видеобаза", "С. Макашов", "Mystery Film", "Arasi project", "Петербургский дубляж", "Толмачев", "Kerob", "SorzTeam", "Flux-Team", "Трамвай-фильм", "NemFilm", "Эй Би Видео", "Alternative Production", "Кинопремьера", "Wayland team", "Первый ТВЧ", "AuraFilm", "Gala Voices", "Sunshine Studio", "SunshineStudio", "GostFilm", "Точка Zрения", "Cowabunga Studio", "AzOnFilm", "AniDUB", "Murzilka", "MaxMeister", "ЕА Синема", "Contentica", "К. Филонов", "Tori", "Inter Video", "Victory-Films", "Тимофеев", "Студия NLS", "Храм Дорам ТВ", "Е. Хрусталёв", "Карапетян", "Наталья Гурзо", "Janetta", "Universal Russia", "HighHopes", "Чуев", "Voice Project", "Emslie", "Lisitz", "Barin101", "Animegroup", "Dream Records", "DreamRecords", "Имидж-Арт", "ko136", "Nastia", "Медиа-Комплекс", "Sound-Group", "Малиновский", "sweet couple", "Sam2007", "SnowRecords", "Horizon Studio", "Family Fan Edition", "TatamiFilm", "Николай Дроздов", "Ultradox", "ImageArt", "Квадрат Малевича", "binjak", "Инфо-фильм", "Мьюзик-трейд", "Сolumbia Service", "Extrabit", "Andre1288", "Максим Жолобов", "turok1990", "Уолт Дисней Компани", "RedDog", "Хабар", "neko64", "Gremlin Creative Studio", "VGM Studio", "Иванова и П. Пашут", "Н. Золотухин", "Костюкевич", "Video-BIZ", "Акалит", "Byako Records", "Агапов", "Мительман", "Persona99", "East Dream", "Epic Team", "VicTeam", "ЛанселаП", "Студия Горького", "Бигыч", "AvePremier", "Jade", "Cuba77", "MifSnaiper", "И. Еремеев", "Акопян", "ssvss", "Индия ТВ", "GoldTeam", "Альтера Парс", "Eurochannel", "OSLIKt", "Eladiel", "TF-AniGroup", "Boльгa", "Azazel", "Студия Пиратского Дубляжа", "FireDub", "XvidClub Studio", "Foxlight", "Гундос", "Paul Bunyan", "OpenDub", "Бахурани", "Абдулов", "The Mike Rec.", "Заугаров", "Amazing Dubbing", "Сергей Дидок", "Студия Колобок", "Ancord", "Анатолий Ашмарин", "Бессонов", "Sedorelli", "DexterTV", "Прайд Продакшн", "AveTurk", "MC Entertainment", "Loginoff", "JoyStudio", "Старый Бильбо", "ГКГ", "NEON Studio", "st.Elrom", "Max Nabokov", "Sound Film", "Twix", "Zone Studio" };

        static HashSet<string> ukrVoices = new HashSet<string> { "QTV", "DniproFilm", "AdrianZP", "LeDoyen", "Цікава Ідея", "Cikava Ideya", "КiT", "Inter", "NLO", "Так Треба Продакшн", "ТакТребаПродакшн", "Новий Канал", "Новый Канал", "BambooUA", "ICTV", "Тоніс", "UA-DUB", "ТеТ", "СТБ", "Postmodern", "НЛО", "Колодій", "В одне рило", "SkiDUB", "Інтер", "DubLiCat", "AAA-Sound", "AAASound", "НеЗупиняйПродакшн", "НеЗупиняйПродакшн", "Ozz TV", "1+1", "Три Крапки", "3 крапки", "Tak Treba Production", "UAVoice", "Интер", "TET", "ПлюсПлюс", "Дніпрофільм", "ArtymKo", "Cinemaker", "sweet.tv", "MelodicVoiceStudio", "FanVoxUA", "UkraineFastDUB", "UFDUB", "CHAS.UA", "Струґачка", "StorieS man", "UATeam", "Гуртом", "UkrDub", "AniUA", "Сокира", "FlameStudio", "HATOSHI", "Sengoku" };
    }
}
