using JacRed.Infrastructure.Trackers.NNMClub;
using Xunit;

namespace JacRed.Tests.NNMClub;

public class NNMClubParserTitleTests
{
    static string PortalPage(string rowHtml) =>
        "<html><head><title>NNM-Club</title></head><body>" +
        "<td valign=\"top\" width=\"70%\">" +
        rowHtml +
        "<div class=\"paginport nav\">pages</div>" +
        "</td></body></html>";

    static string PlineRow(
        string title,
        string date = "16 Июл 2026 16:16:38",
        string topicId = "12345",
        string extraRowText = "",
        bool includeMagnet = true)
    {
        string magnet = includeMagnet
            ? "<a href=\"magnet:?xt=urn:btih:ABCDEF0123456789ABCDEF0123456789ABCDEF01\">m</a>"
            : "";

        return "<table width=\"100%\" class=\"pline\">" +
               $"<tr><td><h2 class=\"substr\"><a class=\"pgenmed\" href=\"viewtopic.php?t={topicId}\">{title}</a></h2></td></tr>" +
               $"<tr><td>{extraRowText}{magnet}" +
               "<span title=\"Раздающих\">&nbsp;10</span>" +
               "<span title=\"Качают\">&nbsp;5</span>" +
               "<span class=\"pcomm bold\">1.5 GB</span>" +
               $"| {date}</span> | <span class=\"tit\">x</span>" +
               "</td></tr></table>";
    }

    [Theory]
    [InlineData(
        "Страна грёз / Dreamland (2019) BDRip",
        "Страна грез",
        "Dreamland",
        2019)]
    [InlineData(
        "Академия монстров / Escuela de Miedo / Cranston Academy: Monster Zone (2020)",
        "Академия монстров",
        "Cranston Academy: Monster Zone",
        2020)]
    public void ForeignCinema_ParsesNameOrigYear(string title, string name, string orig, int year)
    {
        var t = Assert.Single(NNMClubParser.ParseTorrentsFromPage(PortalPage(PlineRow(title)), "10"));
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(orig, t.originalname);
        Assert.Equal(year, t.relased);
        Assert.Equal("nnmclub", t.trackerName);
        Assert.Contains("viewtopic.php?t=12345", t.url);
    }

    [Theory]
    [InlineData("Не одна дома (2024) WEB-DL", "Не одна дома", 2024)]
    public void RuMovie_ParsesNameAndYear(string title, string name, int year)
    {
        var t = Assert.Single(NNMClubParser.ParseTorrentsFromPage(PortalPage(PlineRow(title)), "13"));
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }

    [Theory]
    [InlineData("Тайны следствия (2020) WEBRip", "Тайны следствия", 2020)]
    public void RuSerial_ParsesNameAndYear(string title, string name, int year)
    {
        var t = Assert.Single(NNMClubParser.ParseTorrentsFromPage(PortalPage(PlineRow(title)), "4"));
        Assert.Equal(new[] { "serial" }, t.types);
        Assert.Equal(name, t.name);
        Assert.Equal(year, t.relased);
    }

    [Fact]
    public void ForeignSerial_UsesForeignCinemaParser()
    {
        var t = Assert.Single(NNMClubParser.ParseTorrentsFromPage(
            PortalPage(PlineRow("Тьма / The Dark (2026) WEB-DL")), "3"));
        Assert.Equal(new[] { "serial" }, t.types);
        Assert.Equal("Тьма", t.name);
        Assert.Equal("The Dark", t.originalname);
        Assert.Equal(2026, t.relased);
    }

    [Fact]
    public void Sport_ParsesAsSport()
    {
        var t = Assert.Single(NNMClubParser.ParseTorrentsFromPage(
            PortalPage(PlineRow("MotoGP. Этап 11 (2026) WEBRip")), "24"));
        Assert.Equal(new[] { "sport" }, t.types);
        Assert.DoesNotContain("movie", t.types);
        Assert.Equal("MotoGP. Этап 11", t.name);
        Assert.Equal(2026, t.relased);
    }

    [Fact]
    public void SkipsTrailerInTitle()
    {
        Assert.Empty(NNMClubParser.ParseTorrentsFromPage(
            PortalPage(PlineRow("Фильм (2024) трейлер WEB-DL")), "10"));
    }

    [Fact]
    public void Kids_RequiresCartoonHintInRow()
    {
        string title = "Спина к спине (2020) WEBRip";
        Assert.Empty(NNMClubParser.ParseTorrentsFromPage(
            PortalPage(PlineRow(title, extraRowText: "просто книга")), "7"));

        var t = Assert.Single(NNMClubParser.ParseTorrentsFromPage(
            PortalPage(PlineRow(title, extraRowText: "Продолжительность: 01:20:00 мультфильм")), "7"));
        Assert.Equal(new[] { "multfilm", "multserial" }, t.types);
        Assert.Equal("Спина к спине", t.name);
        Assert.Equal(2020, t.relased);
    }

    [Fact]
    public void Kids_SkipsPdfInTitle()
    {
        Assert.Empty(NNMClubParser.ParseTorrentsFromPage(
            PortalPage(PlineRow("Сказки PDF (2020)", extraRowText: "мульт")), "7"));
    }

    [Fact]
    public void Kids_MatchesDlitelnostWithoutTypo()
    {
        var t = Assert.Single(NNMClubParser.ParseTorrentsFromPage(
            PortalPage(PlineRow("Спина к спине (2020)", extraRowText: "Длительность: 90 мин")), "7"));
        Assert.Equal("Спина к спине", t.name);
    }
}
