using System;
using System.Collections.Generic;
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

        Assert.DoesNotContain("Inter", t.voices);
        Assert.DoesNotContain("Интер", t.voices);
        Assert.DoesNotContain("ukr", t.languages);
    }

    [Theory]
    [InlineData("Друзья / Friends (1994) WEB-DL 1080p | Inter | LostFilm")]
    [InlineData("Друзья / Friends (1994) WEB-DL 1080p | Inter, 1+1, Original")]
    [InlineData("Друзья / Friends (1994) WEB-DL 1080p [Inter]")]
    public void UpdateFullDetails_VoiceAsSeparateWord_IsDetected(string title)
    {
        var t = Update(title);

        Assert.Contains("Inter", t.voices);
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
    public void UpdateFullDetails_VoiceGluedInCamelCase_IsDetected()
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

        Assert.Contains("Inter", t.voices);
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

        Assert.Contains("Интерфильм", t.voices);
        Assert.DoesNotContain("Интер", t.voices);
        Assert.DoesNotContain("ukr", t.languages);
    }
}
