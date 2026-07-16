using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.Bitru;
using Xunit;

namespace JacRed.Tests.Bitru;

public class BitruApiParserUnitTests
{
    const string Host = "https://bitru.org";

    static string ItemJson(
        string category,
        string name,
        string origName = null,
        object year = null,
        string quality = null,
        string other = null,
        string[] subsections = null,
        long id = 123,
        string file = null)
    {
        string yearJson = year == null ? "null" : (year is string ys ? $"\"{ys}\"" : year.ToString());
        string orig = origName == null ? "null" : $"\"{Escape(origName)}\"";
        string q = quality == null ? "null" : $"\"{Escape(quality)}\"";
        string o = other == null ? "null" : $"\"{Escape(other)}\"";
        string sub = subsections == null
            ? "null"
            : "[" + string.Join(",", subsections.Select(s => $"\"{Escape(s)}\"")) + "]";
        string fileJson = file == null ? $"\"{Host}/api.php?download={id}\"" : $"\"{Escape(file)}\"";

        return $$"""
            {
              "result": {
                "after_date": 1,
                "before_date": 1,
                "items": [{
                  "item": {
                    "torrent": {
                      "id": {{id}},
                      "added": 1700000000,
                      "size": 2147483648,
                      "leechers": 2,
                      "seeders": 10,
                      "file": {{fileJson}}
                    },
                    "info": {
                      "name": "{{Escape(name)}}",
                      "year": {{yearJson}}
                    },
                    "template": {
                      "category": "{{Escape(category)}}",
                      "subsection": {{sub}},
                      "orig_name": {{orig}},
                      "other": {{o}},
                      "video": { "quality": {{q}} }
                    }
                  }
                }]
              }
            }
            """;
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [Fact]
    public void ParseTorrentsFromJson_Movie_MapsFields()
    {
        var list = BitruApiParser.ParseTorrentsFromJson(
            ItemJson("movie", "Робин Гуд", "Robin Hood", 1991, "BDRip", "D"),
            Host);

        var t = Assert.Single(list);
        Assert.Equal("bitru", t.trackerName);
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.Equal($"{Host}/details.php?id=123", t.url);
        Assert.Contains("Робин Гуд", t.title);
        Assert.Contains("Robin Hood", t.title);
        Assert.Contains("(1991)", t.title);
        Assert.Equal("Робин Гуд", t.name);
        Assert.Equal("Robin Hood", t.originalname);
        Assert.Equal(1991, t.relased);
        Assert.Equal(10, t.sid);
        Assert.Equal(2, t.pir);
        Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
        Assert.Contains("api.php?download=123", t._sn);
        Assert.NotEqual(default, t.createTime);
    }

    [Fact]
    public void ParseTorrentsFromJson_Serial_AndYearRange()
    {
        var list = BitruApiParser.ParseTorrentsFromJson(
            ItemJson("serial", "Сериал", null, "2011-2015", "WEBRip"),
            Host);

        var t = Assert.Single(list);
        Assert.Equal(new[] { "serial" }, t.types);
        Assert.Equal(2011, t.relased);
        Assert.Contains("(2011-2015)", t.title);
    }

    [Fact]
    public void ParseTorrentsFromJson_VideoDocumovie()
    {
        var list = BitruApiParser.ParseTorrentsFromJson(
            ItemJson("video", "Душа океана", "Soul of the Ocean", "2022", subsections: new[] { "Документальный" }),
            Host);

        var t = Assert.Single(list);
        Assert.Equal(new[] { "documovie" }, t.types);
    }

    [Fact]
    public void ParseTorrentsFromJson_UnknownCategory_Empty()
    {
        Assert.Empty(BitruApiParser.ParseTorrentsFromJson(
            ItemJson("music", "Album"), Host));
    }

    [Fact]
    public void ParseTorrentsFromJson_VideoTrailer_Dropped()
    {
        Assert.Empty(BitruApiParser.ParseTorrentsFromJson(
            ItemJson("video", "Trailer", subsections: new[] { "Трейлер" }), Host));
    }

    [Fact]
    public void ParseTorrentsFromJson_StringError_ReturnsEmpty()
    {
        Assert.Empty(BitruApiParser.ParseTorrentsFromJson("{\"error\":\"max limit 100\"}", Host));
    }

    [Fact]
    public void ParseTorrentsFromJson_EmptyFile_FallsBackToApiDownload()
    {
        var list = BitruApiParser.ParseTorrentsFromJson(
            ItemJson("movie", "Film", year: 2020, file: ""),
            Host);
        var t = Assert.Single(list);
        Assert.Equal($"{Host}/api.php?download=123", t._sn);
    }

    [Theory]
    [InlineData("Название (2020) WEB-DL 1080p", "Название")]
    [InlineData("Show S01E01 720p", "Show")]
    [InlineData("Сериал 1 сезон", "Сериал")]
    public void CleanTitleForSearch_StripsNoise(string input, string expected)
    {
        Assert.Equal(expected, BitruApiParser.CleanTitleForSearch(input));
    }

    [Fact]
    public void UnixFromDate_IsStartOfDayNotEpochZero()
    {
        var today = DateTime.UtcNow.Date;
        long unix = BitruApiParser.UnixFromDate(today);
        Assert.True(unix > 0);
        Assert.Equal(
            new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
            unix);
    }
}
