using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using Xunit;

namespace JacRed.Tests.Indexers;

public class TorznabXmlFormatterLanguageTests
{
    static string Xml(string tracker, string title, params string[] languages) =>
        TorznabXmlFormatter.ItemsXml(new[]
        {
            new Result { Tracker = tracker, Title = title, languages = languages.Length > 0 ? new HashSet<string>(languages) : null }
        }, "5000", false, null);

    static string[] Languages(string xml) =>
        Regex.Matches(xml, "name=\"language\" value=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void ItemsXml_UkrainianTrackerOnly_UkrainianFirstWithoutRussian()
    {
        var xml = Xml("mazepa", "Гра Престолів (Сезон 1-8) / Game of Thrones 2xUkr/Eng", "ukr", "eng");

        Assert.Equal(new[] { "Ukrainian", "English" }, Languages(xml));
        Assert.Contains("name=\"lang\" value=\"ru\"", xml);
    }

    [Fact]
    public void ItemsXml_MergedWithRussianTracker_RussianFirst()
    {
        var xml = Xml("rutor, toloka", "Дюна / Dune (2021) | D, P | UKR", "ukr");

        Assert.Equal(new[] { "Russian", "Ukrainian" }, Languages(xml));
    }

    [Fact]
    public void ItemsXml_KnownLanguagesWithoutRussian_KeepRussianGuess()
    {
        Assert.Equal(new[] { "Russian", "Ukrainian" }, Languages(Xml("kinozal", "Интерстеллар / Interstellar (2014) ДБ", "ukr")));
        Assert.Equal(new[] { "Russian", "English" }, Languages(Xml("nnmclub", "Игра престолов / Game of Thrones (2012) LostFilm", "eng")));
    }

    [Fact]
    public void ItemsXml_LanguageCodes_NormalisedDeduplicatedJunkDropped()
    {
        var xml = Xml("rutracker", "Амели / Le Fabuleux destin d'Amélie Poulain", " RUS ", "fre", "fra", "en-US", "und", "mul", "zxx", "qaa", "kur");

        Assert.Equal(new[] { "Russian", "English", "French", "kur" }, Languages(xml));
    }

    [Theory]
    [InlineData("Игра престолов", "Russian", "ru")]
    [InlineData("Game of Thrones", "English", "en")]
    public void ItemsXml_NoLanguages_AlphabetGuess(string title, string language, string lang)
    {
        var xml = Xml("rutracker", title);

        Assert.Equal(new[] { language }, Languages(xml));
        Assert.Contains($"name=\"lang\" value=\"{lang}\"", xml);
    }

    [Fact]
    public void MapV1_AddsStoredFfprobeAudioLanguages()
    {
        var torrent = new TorrentDetails
        {
            trackerName = "nnmclub",
            title = "Игра престолов",
            languages = new HashSet<string> { "rus" },
            ffprobe = new List<ffStream>
            {
                new ffStream { codec_type = "audio", tags = new ffTags { language = "eng" } },
                new ffStream { codec_type = "subtitle", tags = new ffTags { language = "fre" } }
            }
        };

        var result = IndexerSearchEngine.MapV1(torrent, rqnum: false);

        Assert.Equal(new[] { "eng", "rus" }, result.languages.OrderBy(l => l));
    }
}
