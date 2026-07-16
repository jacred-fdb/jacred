using System.Linq;
using JacRed.Infrastructure.Trackers.Kinozal;
using Xunit;

namespace JacRed.Tests.Kinozal;

public class KinozalParserSportTitleTests
{
    static string BrowseRow(string title) =>
        "<tr class=bg>" +
        "<td class=\"nam\"><a href=\"/details.php?id=1\" class=\"r0\">" + title + "</a></td>" +
        "<td class='s'>0</td>" +
        "<td class='s'>1.5 ГБ</td>" +
        "<td class='sl_s'>10</td>" +
        "<td class='sl_p'>5</td>" +
        "<td class='s'>16.07.2024 в 12:00</td>" +
        "</tr>";

    [Theory]
    [InlineData(
        "Велоспорт. Тур де Франс 2026 (12-й этап) / 2026 / РУ / WEB-DL (1080p)",
        "Велоспорт. Тур де Франс 2026 (12-й этап)",
        2026)]
    [InlineData(
        "Футбол. Лига чемпионов 2026/27 (1-й раунд, 2-й матч) Сутьеска (Черногория) - Кайрат (Казахстан) / 2026 / РУ / WEB-DL (1080p)",
        "Футбол. Лига чемпионов 2026/27 (1-й раунд, 2-й матч) Сутьеска (Черногория) - Кайрат (Казахстан)",
        2026)]
    public void SportTitles_ParseAsSport_WithNameAndYear(string title, string expectedName, int expectedYear)
    {
        var html = "<table>" + BrowseRow(title) + "</table>";
        var torrents = KinozalParser.ParseTorrentsFromPage(html, "37");

        Assert.Single(torrents);
        Assert.Equal(new[] { "sport" }, torrents[0].types);
        Assert.DoesNotContain("movie", torrents[0].types);
        Assert.Equal(expectedName, torrents[0].name);
        Assert.Equal(expectedYear, torrents[0].relased);
    }

    [Fact]
    public void SportFixture_TypedAsSport_AndMostHaveYear()
    {
        string html = FixtureLoader.Read("Kinozal/browse_c37.html");
        var torrents = KinozalParser.ParseTorrentsFromPage(html, "37");

        Assert.True(torrents.Count >= 40);
        Assert.All(torrents, t =>
        {
            Assert.Equal(new[] { "sport" }, t.types);
            Assert.DoesNotContain("movie", t.types);
        });
        int withYear = torrents.Count(t => t.relased > 0);
        Assert.True(withYear >= 40, $"expected most sport torrents to have year, got {withYear}/{torrents.Count}");
    }
}
