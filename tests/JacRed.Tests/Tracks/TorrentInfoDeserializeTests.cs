using JacRed.Infrastructure.Tracks;
using Newtonsoft.Json;
using Xunit;

namespace JacRed.Tests.Tracks;

public class TorrentInfoDeserializeTests
{
    [Fact]
    public void Deserializes_file_stats_from_TorrServer_get_status()
    {
        const string json = """
            {
              "title": "Example",
              "category": "jacred",
              "hash": "aabbccddeeff00112233445566778899aabbccdd",
              "name": "Example.mkv",
              "stat": 3,
              "stat_string": "Torrent working",
              "file_stats": [
                { "id": 1, "path": "Example.mkv", "length": 123456789 },
                { "id": 2, "path": "sample.nfo", "length": 100 }
              ]
            }
            """;

        var info = JsonConvert.DeserializeObject<TracksDB.TorrentInfo>(json);

        Assert.NotNull(info);
        Assert.Equal(3, info.stat);
        Assert.Equal("jacred", info.category);
        Assert.NotNull(info.file_stats);
        Assert.Equal(2, info.file_stats.Count);
        Assert.Equal(1, info.file_stats[0].id);
        Assert.Equal("Example.mkv", info.file_stats[0].path);
        Assert.Equal(123456789, info.file_stats[0].length);
    }

    [Fact]
    public void Deserializes_peer_stats_from_TorrServer_get_status()
    {
        const string json = """
            {
              "hash": "aabbccddeeff00112233445566778899aabbccdd",
              "stat": 3,
              "connected_seeders": 2,
              "active_peers": 5,
              "download_speed": 1024,
              "bytes_read": 65536
            }
            """;

        var info = JsonConvert.DeserializeObject<TracksDB.TorrentInfo>(json);

        Assert.NotNull(info);
        Assert.Equal(2, info.connected_seeders);
        Assert.Equal(5, info.active_peers);
        Assert.Equal(1024, info.download_speed);
        Assert.Equal(65536, info.bytes_read);
    }

    [Fact]
    public void Ready_when_file_stats_non_empty()
    {
        var notReady = new TracksDB.TorrentInfo { stat = 1, file_stats = null };
        var ready = new TracksDB.TorrentInfo
        {
            stat = 2,
            file_stats = new System.Collections.Generic.List<TracksDB.TorrentFileStat>
            {
                new() { id = 1, path = "a.mkv", length = 1 }
            }
        };

        Assert.True(ready.file_stats is { Count: > 0 });
        Assert.False(notReady.file_stats is { Count: > 0 });
    }
}
