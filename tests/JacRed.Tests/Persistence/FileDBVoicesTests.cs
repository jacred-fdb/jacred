using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Tracks;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using Xunit;

namespace JacRed.Tests.Persistence;

/// <summary>
/// updateFullDetails detects voices by name in the title and in ffprobe audio track titles.
/// A voice must match only as a whole word: the Ukrainian channel "Inter" / "Интер"
/// must not be found inside "Interstellar", "Интерны" or "International".
/// </summary>
public class FileDBVoicesTests : IDisposable
{
    const string InfoHash = "0a1b2c3d4e5f60718293a4b5c6d7e8f901234567";
    const string Magnet = "magnet:?xt=urn:btih:" + InfoHash;

    public void Dispose() => TracksAnalyzer.Database.TryRemove(InfoHash, out _);

    static TorrentDetails Details(string title) => new()
    {
        trackerName = "rutor",
        url = "https://rutor.example/1",
        title = title,
        magnet = Magnet,
        types = new[] { "movie" },
    };

    static TorrentDetails Update(string title)
    {
        var t = Details(title);
        FileDB.updateFullDetails(t);
        return t;
    }

    [Theory]
    [InlineData("Интерстеллар / Interstellar (2014) BDRip 1080p")]
    [InlineData("Интерны / Сезон: 1 / Серии: 1-20 из 20 [2010, комедия, HDTVRip]")]
    [InlineData("Люди в черном: Интернэшнл / Men in Black International (2019) WEB-DLRip")]
    [InlineData("Во все тяжкие / Breaking Bad (2008) BDRip | Selena International")]
    public void UpdateFullDetails_VoiceInsideLongerWord_IsNotDetected(string title)
    {
        var t = Update(title);

        Assert.DoesNotContain("Інтер", t.voices);
        Assert.DoesNotContain("ukr", t.languages);
    }

    [Theory]
    [InlineData("Друзья / Friends (1994) WEB-DL 1080p | Inter | LostFilm")]
    [InlineData("Друзья / Friends (1994) WEB-DL 1080p | Inter, 1+1, Original")]
    [InlineData("Друзья / Friends (1994) WEB-DL 1080p [Inter]")]
    public void UpdateFullDetails_VoiceAsSeparateWord_IsDetected(string title)
    {
        var t = Update(title);

        Assert.Contains("Інтер", t.voices);
        Assert.Contains("ukr", t.languages);
    }

    [Fact]
    public void UpdateFullDetails_MultiWordVoice_IsDetected()
    {
        var t = Update("Наруто / Naruto [TV] [220 из 220] (Parovoz Production) WEBRip");

        Assert.Contains("Parovoz Production", t.voices);
    }

    [Fact]
    public void UpdateFullDetails_SurnameInsideInflectedWord_IsNotDetected()
    {
        var t = Update("Снежная королева / The Snow Queen (2012) BDRip");

        Assert.DoesNotContain("Королев", t.voices);
    }

    [Fact]
    public void UpdateFullDetails_GluedSpellingOfVoice_IsDetected()
    {
        var t = Update("Ведьмак (1 сезон: 1-8 серии из 8) / The Witcher / 2019 / ЛМ (KerobTV) / WEBRip");

        Assert.Contains("Kerob", t.voices);
        Assert.Contains("rus", t.languages);
    }

    [Fact]
    public void UpdateFullDetails_AudioTrackTitle_VoiceAsSeparateWord_IsDetected()
    {
        TracksAnalyzer.Database[InfoHash] = new FfprobeModel
        {
            streams = new List<ffStream>
            {
                new() { codec_type = "audio", tags = new ffTags { title = "MVO Inter" } },
            }
        };

        var t = Update("Интерстеллар / Interstellar (2014) BDRip 1080p");

        Assert.Contains("Інтер", t.voices);
        Assert.Contains("ukr", t.languages);
    }

    [Fact]
    public void UpdateFullDetails_AudioTrackTitle_VoiceInsideLongerWord_IsNotDetected()
    {
        TracksAnalyzer.Database[InfoHash] = new FfprobeModel
        {
            streams = new List<ffStream>
            {
                new() { codec_type = "audio", tags = new ffTags { title = "Dub Интерфильм" } },
            }
        };

        var t = Update("Интерстеллар / Interstellar (2014) BDRip 1080p");

        Assert.Contains("Интер Фильм", t.voices);
        Assert.DoesNotContain("Інтер", t.voices);
        Assert.DoesNotContain("ukr", t.languages);
    }

    static List<ffStream> AudioTracks(params string[] titles) =>
        titles.Select(x => new ffStream { codec_type = "audio", tags = new ffTags { title = x } }).ToList();

    [Fact]
    public void UpdateFullDetails_RecordFfprobe_TrackTitleVoiceIsDetected()
    {
        // A synced instance has the tracks in the record, not in its own tracks DB
        var t = Details("Дюна / Dune: Part One (2021) BDRip 1080p");
        t.ffprobe = AudioTracks("Dub | Пифагор", "Original");

        FileDB.updateFullDetails(t);

        Assert.Contains("Пифагор", t.voices);
        Assert.Contains("rus", t.languages);
    }

    [Fact]
    public void UpdateFullDetails_EmptyRecordFfprobe_FallsBackToTracksDb()
    {
        TracksAnalyzer.Database[InfoHash] = new FfprobeModel { streams = AudioTracks("Dub | Пифагор") };
        var t = Details("Дюна / Dune: Part One (2021) BDRip 1080p");
        t.ffprobe = new List<ffStream>();

        FileDB.updateFullDetails(t);

        Assert.Contains("Пифагор", t.voices);
    }

    const string FdbName = "Дюна ffprobe тест", FdbOriginalName = "Dune Ffprobe Test";

    static TorrentDetails FdbRecord()
    {
        var t = Details($"{FdbName} / {FdbOriginalName} (2021) BDRip 1080p");
        t.name = FdbName;
        t.originalname = FdbOriginalName;
        return t;
    }

    [Fact]
    public void AddOrUpdate_FfprobeArrivesLater_VoicesAreRecomputed()
    {
        string key = FileDB.KeyForTorrent(FdbName, FdbOriginalName);
        string path = FileDB.PathForKey(key);
        try
        {
            using var fdb = FileDB.OpenWrite(key);
            fdb.AddOrUpdate(FdbRecord());
            Assert.Empty(fdb.Database[FdbRecord().url].voices);

            var later = FdbRecord();
            later.ffprobe = AudioTracks("Dub | Пифагор");
            fdb.AddOrUpdate(later);

            Assert.Contains("Пифагор", fdb.Database[later.url].voices);
            Assert.Contains("rus", fdb.Database[later.url].languages);
        }
        finally
        {
            using (var fdb = FileDB.OpenWrite(key))
            {
                fdb.Database.Clear();
                fdb.savechanges = true;
            }

            FileDB.RemoveKeyFromMasterDb(key);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void UpdateFullDetails_LongestName_WinsOverItsPart()
    {
        var t = Update("Во все тяжкие / Breaking Bad / Сезон: 2 [2009, США, драма, BDRip 1080p] Dub (Selena International) + MVO (Amedia)");

        Assert.Equivalent(new[] { "Дубляж", "Selena International", "Amedia" }, t.voices, strict: true);
    }

    [Theory]
    [InlineData("Белорусский вокзал (Андрей Смирнов) [1971, драма, BDRip]", "Смирнов")]
    [InlineData("Сваты / Сезон: 7 / Серии: 1-9 из 9 (Андрей Яковлев) [2021, Украина, комедия, WEBRip]", "Яковлев")]
    [InlineData("Ворон / The Crow (1994) BDRip 1080p", "Ворон")]
    public void UpdateFullDetails_NameBeforeTheYear_IsNotAVoice(string title, string name)
    {
        var t = Update(title);

        Assert.DoesNotContain(name, t.voices);
        Assert.Empty(t.voices);
    }

    [Fact]
    public void UpdateFullDetails_LeadingGroupBeforeTheYear_IsAVoice()
    {
        var t = Update("[AniLibria] Магическая битва / Jujutsu Kaisen [TV] (2020) WEBRip 1080p");

        Assert.Contains("AniLibria", t.voices);
    }

    [Fact]
    public void UpdateFullDetails_CommonWord_IsNotAVoice()
    {
        var t = Update("Джокер / Joker (2019) BDRip от Twister & ExKinoRay | Лицензия");

        Assert.Empty(t.voices);
    }

    [Fact]
    public void UpdateFullDetails_ShortName_MatchesOnlyAsWritten()
    {
        Assert.Contains("DEEP", Update("Монолог фармацевта / Kusuriya no Hitorigoto / 2023 / ДБ (DEEP), 2 x ЛМ, СТ / WEB-DL (1080p)").voices);
        Assert.DoesNotContain("DEEP", Update("Глубина / The Deep (2012) Deep Blue BDRip").voices);
    }

    [Theory]
    [InlineData("Доктор Хаус / House M.D. (2004) BDRip | FoxLife", "Fox Life")]
    [InlineData("Ковбой Бибоп / Cowboy Bebop (1998) BDRip | АП (Сербин)", "Ю. Сербин")]
    [InlineData("Атака титанов / Shingeki no Kyojin (2013) WEBRip | Studio Band", "Студийная Банда")]
    public void UpdateFullDetails_OtherSpelling_IsShownUnderOneName(string title, string name)
    {
        var t = Update(title);

        Assert.Equivalent(new[] { name }, t.voices, strict: true);
    }

    [Fact]
    public void UpdateFullDetails_PlusAfterName_IsPartOfIt()
    {
        var t = Update("Индиана Джонс / Raiders of the Lost Ark (1981) BDRip | MVO (НТВ+)");

        Assert.Equivalent(new[] { "НТВ+" }, t.voices, strict: true);
    }

    [Fact]
    public void UpdateFullDetails_StudioTracker_GivesItsVoice()
    {
        var t = Details("Рик и Морти / Rick and Morty (2013) WEB-DL 1080p");
        t.trackerName = "rudub";
        FileDB.updateFullDetails(t);

        Assert.Contains("RuDub", t.voices);
    }

    [Theory]
    [InlineData("Иван Васильевич меняет профессию (Леонид Гайдай) [1973, комедия, BDRip] Мосфильм")]
    [InlineData("Джокер / Joker (2019) BDRip | Русский дубляж")]
    public void UpdateFullDetails_CommonWordOfLanguage_IsNoVoiceButGivesLanguage(string title)
    {
        var t = Update(title);

        Assert.Empty(t.voices);
        Assert.Contains("rus", t.languages);
    }

    [Fact]
    public void UpdateFullDetails_ChannelInTrackTitle_IsTheStudio()
    {
        TracksAnalyzer.Database[InfoHash] = new FfprobeModel { streams = AudioTracks("MVO (Карусель)", "Дубляж IVI", "AniLibria (Dejz, Lupin)") };

        var t = Update("Индиана Джонс / Raiders of the Lost Ark (1981) BDRip");

        Assert.Equivalent(new[] { "Карусель", "IVI", "AniLibria" }, t.voices, strict: true);
    }

    [Fact]
    public void UpdateFullDetails_OnlineCinemaInReleaseTitle_IsTheSource()
    {
        var t = Update("Таксист / Taxi Driver (1976) WEB-DL 1080p | IVI | Kinopoisk HD");

        Assert.Empty(t.voices);
    }

    [Theory]
    [InlineData("Друзья / Friends (1994) WEB-DL 1080p | Inter")]
    [InlineData("Друзья / Friends (1994) WEB-DL 1080p | Интер")]
    [InlineData("Друзья / Friends (1994) WEB-DL 1080p | Інтер")]
    public void UpdateFullDetails_InterSpellings_AreOneChannel(string title)
    {
        var t = Update(title);

        Assert.Equivalent(new[] { "Інтер" }, t.voices, strict: true);
        Assert.Contains("ukr", t.languages);
    }

    [Fact]
    public void UpdateFullDetails_Ozz_IsNotUkrainian()
    {
        var t = Update("Острые козырьки / Peaky Blinders (2013) HDTVRip | Ozz.tv");

        Assert.Equivalent(new[] { "Ozz" }, t.voices, strict: true);
        Assert.Contains("rus", t.languages);
        Assert.DoesNotContain("ukr", t.languages);
    }

    [Fact]
    public void UpdateFullDetails_LeadingSpaceBeforeGroup_IsTheLeadingGroup()
    {
        var t = Update(" [AniLibria] Магическая битва / Jujutsu Kaisen [TV] (2020) WEBRip 1080p");

        Assert.Contains("AniLibria", t.voices);
    }

    [Fact]
    public void UpdateFullDetails_OriginalTrackOfOnlineCinema_IsNoVoice()
    {
        // the online cinema is the source of the original track; a studio in such a title is still the studio
        var t = Details("Индиана Джонс / Raiders of the Lost Ark (1981) WEB-DL 1080p");
        t.ffprobe = AudioTracks("Original AC3 (IVI)", " Оригинал | KinoPoisk HD", "Original (Пифагор)", "Дубляж IVI");

        FileDB.updateFullDetails(t);

        Assert.Equivalent(new[] { "Пифагор", "IVI" }, t.voices, strict: true);
    }

    [Fact]
    public void UpdateFullDetails_OriginalTrackOfOnlineCinemaOnly_IsNoVoice()
    {
        var t = Details("Индиана Джонс / Raiders of the Lost Ark (1981) WEB-DL 1080p");
        t.ffprobe = AudioTracks("Original AC3 (IVI)", "Original E-AC3 (KinoPoisk HD)");

        FileDB.updateFullDetails(t);

        Assert.Empty(t.voices);
    }

    [Theory]
    [InlineData("ТеТ")]
    [InlineData("ТЕТ")]
    [InlineData("TET")]
    public void UpdateFullDetails_ExactNameWithSeveralWrittenForms_MatchesEach(string written)
    {
        var t = Update($"Друзья / Friends (1994) WEB-DL 1080p | {written}");

        Assert.Equivalent(new[] { "ТЕТ" }, t.voices, strict: true);
        Assert.Contains("ukr", t.languages);
    }

    [Fact]
    public void UpdateFullDetails_ExactName_OtherWrittenFormIsNoMatch()
    {
        Assert.Empty(Update("Друзья / Friends (1994) WEB-DL 1080p | Тет").voices);
    }

    [Fact]
    public void UpdateFullDetails_DecomposedLetters_MatchAsComposed()
    {
        // "й" as "и" + U+0306 in track titles; allVoices also holds such a spelling of НеЗупиняйПродакшн
        var t = Details("Дюна / Dune: Part One (2021) BDRip 1080p");
        t.ffprobe = AudioTracks("AVO [А.Карповскии\u0306]", "MVO НеЗупиняи\u0306Продакшн", "MVO НеЗупиняйПродакшн");

        FileDB.updateFullDetails(t);

        Assert.Equivalent(new[] { "А. Карповский", "НеЗупиняйПродакшн" }, t.voices, strict: true);
    }

    static TorrentDetails UpdateWithTracks(params string[] tracks)
    {
        var t = Details("Дюна / Dune: Part One (2021) WEB-DL 1080p");
        t.ffprobe = AudioTracks(tracks);
        FileDB.updateFullDetails(t);
        return t;
    }

    [Theory]
    [InlineData("DUB | Мосфильм-Мастер", "Мосфильм-Мастер", "rus")]
    [InlineData("DUB AC3 (Mosfilm-Master)", "Мосфильм-Мастер", "rus")]
    [InlineData("MVO | Робота голосом", "Робота Голосом", "ukr")]
    [InlineData("MVO | 15K3 для MGG", "15КЗ", "ukr")]
    [InlineData("MVO | Інтер-фільм", "Інтер-фільм", "rus")]
    public void UpdateFullDetails_NewStudio_IsDetectedWithItsLanguage(string track, string name, string language)
    {
        var t = UpdateWithTracks(track);

        Assert.Contains(name, t.voices);
        Assert.Equivalent(new[] { language }, t.languages, strict: true);
    }

    [Fact]
    public void UpdateFullDetails_StudioOfBothLanguages_GivesNoLanguage()
    {
        var t = UpdateWithTracks("MVO [Syncmer]");

        Assert.Equivalent(new[] { "Syncmer" }, t.voices, strict: true);
        Assert.Empty(t.languages);
    }

    [Theory]
    [InlineData("MVO CTC", "СТС")]
    [InlineData("DVO - KOLOBOK", "Колобок")]
    [InlineData("DUB (Видео Продакшн / iTunes)", "Видеопродакшн")]
    [InlineData("AVO Anton Alekseev", "А. Алексеев")]
    [InlineData("AVO [kyberpunk]", "М. Яроцкий")]
    [InlineData("Dub, Iyuno-SDI Group", "SDI Media")]
    public void UpdateFullDetails_NewSpellingOfKnownName_IsShownUnderIt(string track, string name)
    {
        Assert.Equivalent(new[] { name }, UpdateWithTracks(track).voices, strict: true);
    }

    [Theory]
    [InlineData("MVO (OKKO)", "Okko")]
    [InlineData("UKR (Новий)", "Новий Канал")]
    [InlineData("Ukrainian | MVO | ТК \"ДІМ\"", "Дім")]
    [InlineData("MVO Россия", "Россия")]
    public void UpdateFullDetails_ChannelOrCinemaInTrackTitle_IsTheStudio(string track, string name)
    {
        Assert.Equivalent(new[] { name }, UpdateWithTracks(track).voices, strict: true);
    }

    [Theory]
    [InlineData("Original EAC3 (OKKO)")]
    [InlineData("Ukrainian | новий переклад")]
    [InlineData("Студія \"Медіа Дім \"РАВА\"\"")]
    public void UpdateFullDetails_ChannelWordInTrackTitle_IsNoStudio(string track)
    {
        Assert.Empty(UpdateWithTracks(track).voices);
    }

    [Theory]
    [InlineData("Трансформеры / Transformers (2007) WEB-DL 1080p | OKKO")]
    [InlineData("Трансформеры / Transformers (2007) WEB-DL 1080p | Megogo")]
    [InlineData("Mushoku Tensei III / Реинкарнация безработного [ТВ-3] (14 из 14) Complete [1080p]")]
    public void UpdateFullDetails_ChannelOrCinemaInReleaseTitle_IsNoVoice(string title)
    {
        var t = Update(title);

        Assert.Empty(t.voices);
        Assert.Empty(t.languages);
    }

    [Theory]
    [InlineData("DUB (iTunes)")]
    [InlineData("Dub Netflix")]
    public void UpdateFullDetails_SourceOfTheFile_IsNeverAVoice(string track)
    {
        Assert.Empty(UpdateWithTracks(track).voices);
    }

    [Theory]
    [InlineData("AVO V.Popov", "А. Попов")]
    [InlineData("AVO Алексей Попов", "А. Попов")]
    [InlineData("VO Егор Хрусталёв", "М. Латышев")]
    [InlineData("VO Solod", "Е. Солодухин")]
    [InlineData("MVO Neoclassica", "Неоклассика")]
    [InlineData("VO Петербуржец", "В. Козлов")]
    [InlineData("VO Владимир Козлов", "В. Козлов")]
    [InlineData("VO Сергей Козлов", "С. Козлов")]
    [InlineData("VO Козлов", "Козлов")]
    public void UpdateFullDetails_PersonOrStudioUnderOtherName_IsShownUnderOneName(string track, string name)
    {
        Assert.Equivalent(new[] { name }, UpdateWithTracks(track).voices, strict: true);
    }

    [Fact]
    public void UpdateFullDetails_TwoPeopleOfOneTrack_AreBothFound()
    {
        Assert.Equivalent(new[] { "П. Гланц", "И. Королёва" }, UpdateWithTracks("DVO, Гланц и Королёва").voices, strict: true);
    }

    [Theory]
    [InlineData("MVO СТС со вставками В. Котова")]
    [InlineData("Dub, BD CEE")]
    [InlineData("BD USA Paramount Pictures")]
    [InlineData("MVO FocusStudio (Михаил Хрусталев, Анна Ветрова)")]
    public void UpdateFullDetails_SourceOrListedActor_IsNoVoice(string track)
    {
        var voices = UpdateWithTracks(track).voices;

        Assert.DoesNotContain("BD CEE", voices);
        Assert.DoesNotContain("Paramount Pictures", voices);
        Assert.DoesNotContain("Котова", voices);
        Assert.DoesNotContain("М. Латышев", voices);
    }

    [Fact]
    public void UpdateFullDetails_StudioOfSeveralLanguages_IsNotRussian()
    {
        var t = UpdateWithTracks("Dub | Cinema Tone Production");

        Assert.Equivalent(new[] { "Cinema Tone Production" }, t.voices, strict: true);
        Assert.Empty(t.languages);
    }

    [Theory]
    [InlineData("FoxLife", "Fox Life")]
    [InlineData("Сербин", "Ю. Сербин")]
    [InlineData("LostFilm", "LostFilm")]
    [InlineData("Unknown Studio", "Unknown Studio")]
    public void CanonicalVoice_OtherSpelling_GivesNameShown(string name, string expected)
    {
        Assert.Equal(expected, FileDB.CanonicalVoice(name));
    }
}
