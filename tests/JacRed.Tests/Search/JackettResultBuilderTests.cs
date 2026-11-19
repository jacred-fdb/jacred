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

    static TorrentDetails Seeded(string tracker, int ageMinutes, int sid, int pir)
    {
        var t = Torrent(tracker, ageMinutes, null, null);
        t.sid = sid;
        t.pir = pir;
        return t;
    }

    [Theory]
    [InlineData("korsars")]
    [InlineData("selezen")]
    public void Build_MergeDuplicates_IgnoresInflatedSeedersOfNewestRecord(string tracker)
    {
        // The newest record becomes the merge target; its inflated counts must not survive.
        var inflated = Seeded(tracker, 0, 231, 50);
        var kinozal = Seeded("kinozal", 1, 4, 2);
        var rutor = Seeded("rutor", 2, 1, 3);

        var result = Assert.Single(JackettResultBuilder.Build(Db(inflated, kinozal, rutor), null, false));

        Assert.Equal(4, result.Seeders);
        Assert.Equal(3, result.Peers);
    }

    [Fact]
    public void Build_MergeDuplicates_KeepsSeedersWhenOnlyUnreliableTracker()
    {
        var korsars = Seeded("korsars", 0, 231, 50);

        var result = Assert.Single(JackettResultBuilder.Build(Db(korsars), null, false));

        Assert.Equal(231, result.Seeders);
        Assert.Equal(50, result.Peers);
    }

    [Fact]
    public void Build_MergeDuplicates_TakesMaxSeedersOfReliableTrackers()
    {
        var rutor = Seeded("rutor", 0, 5, 1);
        var kinozal = Seeded("kinozal", 1, 7, 0);
        var korsars = Seeded("korsars", 2, 1700, 90);

        var result = Assert.Single(JackettResultBuilder.Build(Db(rutor, kinozal, korsars), null, false));

        Assert.Equal(7, result.Seeders);
        Assert.Equal(1, result.Peers);
    }

    // A renamed topic: the old record stays in its old bucket and under an old domain, here with a later createTime.
    static TorrentDetails Copy(string tracker, string url, string title, DateTime createTime, DateTime updateTime, int sid) => new()
    {
        trackerName = tracker,
        url = url,
        title = title,
        name = title,
        magnet = Magnet,
        types = new[] { "anime" },
        sid = sid,
        createTime = createTime,
        updateTime = updateTime,
    };

    static readonly DateTime Renamed = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_MergeDuplicates_TakesRecordOfLaterCopyOnSameTracker()
    {
        var old = Copy("rutracker", "https://rutracker.net/forum/viewtopic.php?t=1", "Old title [TV+Special]", new DateTime(2020, 5, 6), Renamed, 6);
        var fresh = Copy("rutracker", "https://rutracker.org/forum/viewtopic.php?t=1", "New title (TV-1) [TV+Special]", new DateTime(2016, 7, 25), Renamed.AddMinutes(1), 12);

        var result = Assert.Single(JackettResultBuilder.Build(Db(old, fresh), null, false));

        Assert.Equal("New title (TV-1) [TV+Special]", result.Title);
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=1", result.Details);
        Assert.Equal("New title (TV-1) [TV+Special]", result.info.name);
        Assert.Equal(Renamed.AddMinutes(1), result.UpdateTime);
        Assert.Equal(12, result.Seeders);
        Assert.Equal("Old title [TV+Special]", old.title);
    }

    [Fact]
    public void Build_MergeDuplicates_KeepsNewestRecordOnEqualUpdateTime()
    {
        var newest = Copy("rutracker", "https://rutracker.net/forum/viewtopic.php?t=1", "Newest", new DateTime(2020, 5, 6), Renamed, 6);
        var other = Copy("rutracker", "https://rutracker.org/forum/viewtopic.php?t=1", "Other", new DateTime(2016, 7, 25), Renamed, 12);

        var result = Assert.Single(JackettResultBuilder.Build(Db(other, newest), null, false));

        Assert.Equal("Newest", result.Title);
    }

    [Fact]
    public void Build_MergeDuplicates_KeepsRecordForLaterCopyOfOtherTracker()
    {
        var rutracker = Copy("rutracker", "https://rutracker.org/forum/viewtopic.php?t=1", "Rutracker title", new DateTime(2020, 5, 6), Renamed, 6);
        var rutor = Copy("rutor", "https://rutor.info/torrent/1", "Rutor title", new DateTime(2016, 7, 25), Renamed.AddDays(1), 12);

        var result = Assert.Single(JackettResultBuilder.Build(Db(rutracker, rutor), null, false));

        Assert.Equal("Rutracker title", result.Title);
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=1", result.Details);
    }
}
