using JacRed.Infrastructure.Trackers.TorrentBy;
using Xunit;

namespace JacRed.Tests.TorrentBy;

public class TorrentByParserTitleTests
{
    static string BrowseRow(string title, string date = "2024-07-16") =>
        "<tr class=\"ttable_col1\">" +
        $"<td style=\"white-space:nowrap;text-align:center;\">{date}</td>" +
        "<td style=\"white-space:nowrap;\">" +
        "<a class=\"magnet\" href=\"magnet:?xt=urn:btih:ABCDEF0123456789ABCDEF0123456789ABCDEF01\">m</a> " +
        $"<a name=\"search_select\" style=\"float:left;\" href=\"/123/slug\">{title}</a>" +
        "</td>" +
        "<td style=\"white-space:nowrap;\">1.5 GB</td>" +
        "<td><font color=\"green\">&uarr; 10</font> <font color=\"red\">&darr; 5</font></td>" +
        "</tr>";

    [Theory]
    [InlineData(
        "films",
        "Запретный плод / Forbidden Fruits (2025) WEB-DLRip 720p от New-Team",
        "Запретный плод",
        "Forbidden Fruits",
        2025)]
    [InlineData(
        "films",
        "Только течёт река / He bian de cuo wu / Only the River Flows (2023) BDRip 720p",
        "Только течет река",
        "Only the River Flows",
        2023)]
    public void FilmsForeign_ParsesNameOrigYear(string cat, string title, string name, string orig, int year)
    {
        var t = Assert.Single(TorrentByParser.ParseTorrentsFromHtml("<table>" + BrowseRow(title) + "</table>", cat));
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(orig, t.originalname);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData("Не одна дома 3. Выпускной (2026) WEB-DL 1080p", "Не одна дома 3. Выпускной", 2026)]
    [InlineData("Фронт в тылу врага / Серии: 1-2 из 2 [1981, драма, военный, WEBRip-AVC]", "Фронт в тылу врага", 1981)]
    public void FilmsRu_ParsesNameAndYear(string title, string name, int year)
    {
        var t = Assert.Single(TorrentByParser.ParseTorrentsFromHtml("<table>" + BrowseRow(title) + "</table>", "movies"));
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData(
        "Дом Дракона / House of the Dragon (2026) WEB-DLRip [H.264/1080p]",
        "Дом Дракона",
        "House of the Dragon",
        2026)]
    [InlineData(
        "Тьма / The Dark [S01] (2026) WEB-DLRip-AVC | L | RuDub",
        "Тьма",
        "The Dark",
        2026)]
    public void SerialForeign_ParsesNameOrigYear(string title, string name, string orig, int year)
    {
        var t = Assert.Single(TorrentByParser.ParseTorrentsFromHtml("<table>" + BrowseRow(title) + "</table>", "serials"));
        Assert.Equal(new[] { "serial" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(orig, t.originalname);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData(
        "Доктор, я боюсь / Сезон: 1 / Серии: 1-36 из 40 [2025-2026, мелодрама, WEBRip-AVC]",
        "Доктор, я боюсь",
        2025)]
    [InlineData("Холод [01х01 из 10] (2026) WEB-DL 1080p", "Холод", 2026)]
    public void SerialRu_ParsesNameAndYear(string title, string name, int year)
    {
        var t = Assert.Single(TorrentByParser.ParseTorrentsFromHtml("<table>" + BrowseRow(title) + "</table>", "series"));
        Assert.Equal(new[] { "serial" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData(
        "Футбол. Чемпионат Мира 2026. 1/2 финала. Англия – Аргентина + Превью [15.07] (2025) WEBRip 1080p",
        "Футбол. Чемпионат Мира 2026. 1/2 финала. Англия – Аргентина + Превью",
        2025)]
    [InlineData(
        "Футбол. Чемпионат Мира 2026. Лучшие голы чемпионата мира-2026 [14.07] (2026) WEBRip 1080р",
        "Футбол. Чемпионат Мира 2026. Лучшие голы чемпионата мира-2026",
        2026)]
    public void Sport_ParsesAsSport_WithNameAndYear(string title, string name, int year)
    {
        var t = Assert.Single(TorrentByParser.ParseTorrentsFromHtml("<table>" + BrowseRow(title) + "</table>", "sport"));
        Assert.Equal(new[] { "sport" }, t.types);
        Assert.DoesNotContain("movie", t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }
}
