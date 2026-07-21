using System.Collections.Generic;
using JacRed.Infrastructure.Tracks;
using Xunit;

namespace JacRed.Tests.Tracks;

public class TracksMediaFileSelectorTests
{
    [Fact]
    public void SelectFileIds_picks_largest_mkv_not_first_txt()
    {
        var files = new List<TracksDB.TorrentFileStat>
        {
            new() { id = 1, path = "readme.txt", length = 100 },
            new() { id = 2, path = "Sample.mkv", length = 50_000_000 },
            new() { id = 3, path = "Movie.mkv", length = 8_000_000_000 },
            new() { id = 4, path = "subs.srt", length = 5000 }
        };

        var ids = TracksMediaFileSelector.SelectFileIds(files, 3);

        Assert.Equal(3, ids[0]);
        Assert.Equal(2, ids[1]);
        Assert.DoesNotContain(1, ids);
    }

    [Fact]
    public void SelectFileIds_excludes_sample_in_path()
    {
        var files = new List<TracksDB.TorrentFileStat>
        {
            new() { id = 1, path = "release.sample.mkv", length = 10_000_000 },
            new() { id = 2, path = "film.mkv", length = 5_000_000_000 }
        };

        var ids = TracksMediaFileSelector.SelectFileIds(files, 2);

        Assert.Equal(2, ids[0]);
        Assert.Single(ids);
    }

    [Fact]
    public void SelectFileIds_fallback_to_largest_when_no_video_ext()
    {
        var files = new List<TracksDB.TorrentFileStat>
        {
            new() { id = 1, path = "a.bin", length = 100 },
            new() { id = 2, path = "b.bin", length = 9000 }
        };

        var ids = TracksMediaFileSelector.SelectFileIds(files, 1);

        Assert.Equal(2, ids[0]);
    }

    [Fact]
    public void SelectFileIds_empty_returns_id_1()
    {
        var ids = TracksMediaFileSelector.SelectFileIds(null, 3);
        Assert.Equal(new[] { 1 }, ids);
    }

    [Fact]
    public void IsVideoCandidate_recognizes_mkv()
    {
        Assert.True(TracksMediaFileSelector.IsVideoCandidate("Season 1/Episode.mkv"));
        Assert.False(TracksMediaFileSelector.IsVideoCandidate("info.nfo"));
    }
}
