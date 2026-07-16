using JacRed.Infrastructure.Trackers.Rutracker;
using Xunit;

namespace JacRed.Tests.Rutracker;

public class RutrackerParserTitleTests
{
    static string ForumRow(string title, string date = "2024-07-16 12:00") =>
        "class=\"torTopic\"\n" +
        $"<a id=\"tt-12345\" href=\"viewtopic.php?t=12345\">{title}</a>\n" +
        "<span class=\"seedmed\" title=\"Seeders\"><b>10</b></span>\n" +
        "<span class=\"leechmed\" title=\"Leechers\"><b>5</b></span>\n" +
        "<a class=\"dl-stub\">1.5&nbsp;GB</a>\n" +
        $"<p>{date}</p>\n";

    [Theory]
    [InlineData(
        "1950",
        "Белый тигр / The White Tiger (Рамин Бахрани / Ramin Bahrani) [2021, Индия, США, драма, WEB-DLRip]",
        "Белый тигр",
        "The White Tiger",
        2021)]
    [InlineData(
        "709",
        "Гунда / Gunda (Виктор Косаковский) [2020, Норвегия, США, документальный, HDRip]",
        "Гунда",
        "Gunda",
        2020)]
    public void Movie_ParsesNameOrigYear(string cat, string title, string name, string orig, int year)
    {
        var t = Assert.Single(RutrackerParser.ParseTorrentsFromPage(ForumRow(title), cat));
        Assert.Equal(RutrackerCategories.Map[cat].Types, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(orig, t.originalname);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData(
        "842",
        "Укрытие / Silo / Сезон: 3 / Серии: 1-2 из 10 (Берт) [2026, США, фантастика, WEB-DLRip]",
        "Укрытие",
        "Silo",
        2026)]
    public void Serial_ParsesNameOrigYear(string cat, string title, string name, string orig, int year)
    {
        var t = Assert.Single(RutrackerParser.ParseTorrentsFromPage(ForumRow(title), cat));
        Assert.Equal(new[] { "serial" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(orig, t.originalname);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData(
        "1105",
        "Благоухающий цветок / Kaoru Hana wa Rin to Saku [2025, TV, 1080p]",
        "Благоухаюший цветок",
        2025)]
    [InlineData(
        "24",
        "Шоу Бенни Хилла - 76 выпусков (Benny Hill Show) [1979, Комедийное шоу, DVDRip]",
        "Шоу Бенни Хилла - 76 выпусков",
        1979)]
    public void NonStandard_ParsesNameAndYear(string cat, string title, string name, int year)
    {
        var t = Assert.Single(RutrackerParser.ParseTorrentsFromPage(ForumRow(title), cat));
        Assert.Equal(RutrackerCategories.Map[cat].Types, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData(
        "Футбол. Чемпионат мира [2026, Спорт, WEBRip]",
        "Футбол. Чемпионат мира",
        2026)]
    [InlineData(
        "XXXII Летние Олимпийские Игры / Церемония закрытия [2021, Олимпиада, IPTV]",
        "XXXII Летние Олимпийские Игры",
        2021)]
    public void Sport_ParsesAsSport_WithYearInBrackets(string title, string name, int year)
    {
        var t = Assert.Single(RutrackerParser.ParseTorrentsFromPage(ForumRow(title), "1392"));
        Assert.Equal(new[] { "sport" }, t.types);
        Assert.DoesNotContain("movie", t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }
}
