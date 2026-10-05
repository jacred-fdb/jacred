using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Application.Search;
using JacRed.Models.Details;
using Xunit;

namespace JacRed.Tests.Search;

/// <summary>
/// Build gets live FileDB cache records (evercache); searching must not mutate them,
/// otherwise concurrent searches over the same key race on the same HashSet.
/// </summary>
public class JackettResultBuilderTests
{
    const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

    static TorrentDetails Torrent(string tracker, int ageMinutes, HashSet<string> voices, HashSet<string> languages) => new()
    {
        trackerName = tracker,
        url = $"https://{tracker}.example/1",
        title = "Shogun S01",
        magnet = Magnet,
        types = new[] { "serial" },
        createTime = new DateTime(2026, 10, 1).AddMinutes(-ageMinutes),
        voices = voices,
        languages = languages,
    };

    static Dictionary<string, TorrentDetails> Db(params TorrentDetails[] torrents) =>
        torrents.ToDictionary(t => t.url);

    [Fact]
    public void Clone_CopiesVoicesAndLanguages()
    {
        var t = Torrent("rutor", 0, new HashSet<string> { "LostFilm" }, new HashSet<string> { "rus" });

        var clone = (TorrentDetails)t.Clone();
        clone.voices.Add("HDRezka");
        clone.languages.Add("ukr");

        Assert.Equal(new[] { "LostFilm" }, t.voices);
        Assert.Equal(new[] { "rus" }, t.languages);
    }

    [Fact]
    public void Build_MergeDuplicates_DoesNotMutateSourceRecords()
    {
        // Newest first: it becomes the merge target, and it has no voices of its own.
        var a = Torrent("rutor", 0, null, null);
        var b = Torrent("rutracker", 1, new HashSet<string> { "LostFilm" }, new HashSet<string> { "rus" });
        var c = Torrent("nnmclub", 2, new HashSet<string> { "HDRezka" }, new HashSet<string> { "ukr" });

        var result = Assert.Single(JackettResultBuilder.Build(Db(a, b, c), null, false));

        Assert.Equal(new[] { "HDRezka", "LostFilm" }, result.info.voices.OrderBy(v => v));
        Assert.Null(a.voices);
        Assert.Null(a.languages);
        Assert.Equal(new[] { "LostFilm" }, b.voices);
        Assert.Equal(new[] { "rus" }, b.languages);
        Assert.Equal(new[] { "HDRezka" }, c.voices);
        Assert.Equal(new[] { "ukr" }, c.languages);
    }
}
