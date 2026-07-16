using JacRed.Infrastructure.Trackers.Rutor;
using Xunit;

namespace JacRed.Tests.Rutor;

public class RutorParserTitleTests
{
    static string BrowseRow(string title, string date = "16.07.24") =>
        "<tr class=\"gai\">" +
        $"<td>{date}</td><td><a class=\"downgif\" href=\"/download/1\"></a>" +
        "<a href=\"magnet:?xt=urn:btih:ABCDEF0123456789ABCDEF0123456789ABCDEF01\">m</a> " +
        $"<a href=\"/torrent/123/slug\">{title}</a></td>" +
        "<td align=\"right\">1.5&nbsp;GB</td>" +
        "<td><span class=\"green\"><img src=\"x.gif\" alt=\"S\" />&nbsp;10</span> " +
        "<span class=\"red\">&nbsp;5</span></td>" +
        "</tr>";

    [Theory]
    [InlineData(
        "Опасное небо / Top Gunner (2020) WEB-DL 1080p | P",
        "Опасное небо",
        "Top Gunner",
        2020)]
    [InlineData(
        "Только течёт река / He bian de cuo wu / Only the River Flows (2023) BDRip 720p",
        "Только течёт река",
        "Only the River Flows",
        2023)]
    public void ForeignMovie_ParsesNameOrigYear(string title, string name, string orig, int year)
    {
        var t = Assert.Single(RutorParser.ParseTorrentsFromPage("<table>" + BrowseRow(title) + "</table>", "1"));
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(orig, t.originalname);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData("Не одна дома 3. Выпускной (2026) WEB-DL 1080p", "Не одна дома 3. Выпускной", 2026)]
    public void RuMovie_ParsesNameAndYear(string title, string name, int year)
    {
        var t = Assert.Single(RutorParser.ParseTorrentsFromPage("<table>" + BrowseRow(title) + "</table>", "5"));
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData(
        "Дом Дракона / House of the Dragon [03x01-04 из 08] (2026) WEB-DL 1080p",
        "Дом Дракона",
        "House of the Dragon",
        2026)]
    public void ForeignSerial_ParsesNameOrigYear(string title, string name, string orig, int year)
    {
        var t = Assert.Single(RutorParser.ParseTorrentsFromPage("<table>" + BrowseRow(title) + "</table>", "4"));
        Assert.Equal(new[] { "serial" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(orig, t.originalname);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData("Холод [01х01 из 10] (2026) WEB-DL 1080p", "Холод", 2026)]
    public void RuSerial_ParsesNameAndYear(string title, string name, int year)
    {
        var t = Assert.Single(RutorParser.ParseTorrentsFromPage("<table>" + BrowseRow(title) + "</table>", "16"));
        Assert.Equal(new[] { "serial" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData(
        "Футбол. Чемпионат Мира 2026. Финал. Англия – Аргентина [15.07] (2026) WEBRip 1080p",
        "Футбол. Чемпионат Мира 2026. Финал. Англия – Аргентина",
        2026)]
    public void Sport_ParsesAsSport_WithNameAndYear(string title, string name, int year)
    {
        var t = Assert.Single(RutorParser.ParseTorrentsFromPage("<table>" + BrowseRow(title) + "</table>", "13"));
        Assert.Equal(new[] { "sport" }, t.types);
        Assert.DoesNotContain("movie", t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }

    [Fact]
    public void Cat17_RequiresUkrInTitle()
    {
        string without = BrowseRow("Some Film / Some Film (2024) WEB-DL");
        string withUkr = BrowseRow("Some Film / Some Film (2024) WEB-DL | UKR");

        Assert.Empty(RutorParser.ParseTorrentsFromPage("<table>" + without + "</table>", "17"));

        var t = Assert.Single(RutorParser.ParseTorrentsFromPage("<table>" + withUkr + "</table>", "17"));
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.Contains(" UKR", t.title);
    }

    [Fact]
    public void SkipsTrailerAndKpk()
    {
        Assert.Empty(RutorParser.ParseTorrentsFromPage(
            "<table>" + BrowseRow("Фильм (2024) трейлер WEB-DL") + "</table>", "5"));
        Assert.Empty(RutorParser.ParseTorrentsFromPage(
            "<table>" + BrowseRow("Фильм (2024) КПК WEB-DL") + "</table>", "5"));
    }
}
