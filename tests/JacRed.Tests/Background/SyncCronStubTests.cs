using JacRed.Infrastructure.Background;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using JacRed.Models.Sync.v2;
using Newtonsoft.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace JacRed.Tests.Background;

/// <summary>
/// /sync/fdb/torrents sends records the client already has (spidr, or updateTime older than
/// <c>start</c>) as stubs: only sid, pir and url. A stub has no name, so it can only be matched
/// by url inside the shard of its collection.
/// </summary>
// Compares masterDb keys before and after, so no other test may add keys meanwhile.
[Collection("FastDb index")]
public class SyncCronStubTests : IDisposable
{
    const string RuName = "Одиссея синк тест";
    const string EnName = "The Odyssey Sync Test";
    const string UrlPrefix = "http://rutor.example.test/torrent/";
    static readonly DateTime UpdateTime = new DateTime(2026, 11, 8, 9, 16, 0, DateTimeKind.Utc);

    readonly string _key = FileDB.KeyForTorrent(RuName, EnName);
    readonly List<string> _cleanupKeys = new();

    public SyncCronStubTests()
    {
        _cleanupKeys.Add(_key);

        using var fdb = FileDB.OpenWrite(_key);
        fdb.AddOrUpdate(Full("1113507", sid: 0, pir: 0));
        fdb.AddOrUpdate(Full("1113519", sid: 0, pir: 0));
    }

    public void Dispose()
    {
        foreach (var key in _cleanupKeys)
        {
            try
            {
                using (var fdb = FileDB.OpenWrite(key))
                {
                    fdb.Database.Clear();
                    fdb.savechanges = true;
                }
            }
            catch
            {
                // ignore cleanup races
            }

            FileDB.RemoveKeyFromMasterDb(key);

            try
            {
                string path = FileDB.PathForKey(key);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }

    static TorrentDetails Full(string id, int sid, int pir) => new()
    {
        url = UrlPrefix + id,
        trackerName = "rutor",
        types = new[] { "movie" },
        title = $"{RuName} / {EnName} (2026) BDRip 1080p {id}",
        name = RuName,
        originalname = EnName,
        magnet = $"magnet:?xt=urn:btih:{id.PadLeft(40, '0')}",
        sid = sid,
        pir = pir,
        sizeName = "10 GB",
        relased = 2026,
        createTime = UpdateTime,
        updateTime = UpdateTime
    };

    // The same object SyncController builds for spidr and for records older than start.
    static TorrentDetails Stub(string id, int sid, int pir) => new() { url = UrlPrefix + id, sid = sid, pir = pir };

    static RootObject Batch(string key, params TorrentDetails[] torrents) => new()
    {
        countread = torrents.Length,
        collections = new List<Collection>
        {
            new()
            {
                Key = key,
                Value = new Value
                {
                    time = UpdateTime,
                    fileTime = UpdateTime.ToFileTimeUtc(),
                    torrents = torrents.ToDictionary(t => t.url)
                }
            }
        }
    };

    TorrentDetails Record(string id) => FileDB.OpenRead(_key)[UrlPrefix + id];

    void Seed(TorrentDetails t)
    {
        using var fdb = FileDB.OpenWrite(_key);
        fdb.AddOrUpdate(t);
    }

    // Server: System.Text.Json with the options from Program.cs. Client: HttpClient.Get uses JsonConvert.
    static RootObject OverTheWire(RootObject batch)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null
        };
        string json = System.Text.Json.JsonSerializer.Serialize(new { nextread = false, batch.countread, take = 2_000, batch.collections }, options);
        return JsonConvert.DeserializeObject<RootObject>(json);
    }

    [Fact]
    public void ApplyTorrents_Stub_UpdatesSeedersOfExistingRecord()
    {
        // The shard is sent again because a new release was added; old records come as stubs.
        SyncCron.ApplyTorrents(Batch(_key,
            Stub("1113507", sid: 716, pir: 364),
            Stub("1113519", sid: 124, pir: 97),
            Full("1113636", sid: 75, pir: 213)));

        var t = Record("1113507");
        Assert.Equal(716, t.sid);
        Assert.Equal(364, t.pir);
        Assert.Equal(124, Record("1113519").sid);
        Assert.Equal(97, Record("1113519").pir);
        Assert.Equal(75, Record("1113636").sid);
        Assert.Equal(3, FileDB.OpenRead(_key).Count);

        // Only the counts change: the stub carries nothing else.
        Assert.Equal(UpdateTime, t.updateTime);
        Assert.Equal("rutor", t.trackerName);
        Assert.Equal(RuName, t.name);
        Assert.Equal(EnName, t.originalname);
        Assert.StartsWith(RuName, t.title);
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.StartsWith("magnet:", t.magnet);
    }

    [Fact]
    public void ApplySpidr_Stub_UpdatesSeedersOfExistingRecord()
    {
        var checkTime = Record("1113507").checkTime;
        var shardTime = FileDB.masterDb[_key].updateTime;

        SyncCron.ApplySpidr(Batch(_key, Stub("1113507", sid: 716, pir: 364)));

        var t = Record("1113507");
        Assert.Equal(716, t.sid);
        Assert.Equal(364, t.pir);
        Assert.Equal(UpdateTime, t.updateTime);
        Assert.Equal(checkTime, t.checkTime);
        Assert.Equal(shardTime, FileDB.masterDb[_key].updateTime);
        Assert.Equal("rutor", t.trackerName);
        Assert.Equal(0, Record("1113519").sid);
    }

    [Fact]
    public void StubFromServerJson_IsRecognisedAndApplied()
    {
        var root = OverTheWire(Batch(_key, Stub("1113507", sid: 716, pir: 364), Full("1113636", sid: 75, pir: 213)));

        // Null fields are not written, so the client gets null name/magnet/types back.
        var stub = root.collections.Single().Value.torrents[UrlPrefix + "1113507"];
        Assert.Null(stub.name);
        Assert.Null(stub.magnet);
        Assert.Null(stub.types);

        var checkTime = Record("1113507").checkTime;
        var shardTime = FileDB.masterDb[_key].updateTime;

        Assert.Equal(1, SyncCron.ApplyTorrents(root));

        var t = Record("1113507");
        Assert.Equal(716, t.sid);
        Assert.Equal(364, t.pir);
        Assert.Equal(UpdateTime, t.updateTime);
        Assert.Equal(checkTime, t.checkTime);
        Assert.Equal(shardTime, FileDB.masterDb[_key].updateTime);
        Assert.Equal(75, Record("1113636").sid);
    }

    [Fact]
    public void Stub_ForRecordExcludedBySyncFilters_ChangesNothing()
    {
        // A client that parses a tracker itself and excludes it from synctrackers keeps its own counts.
        var sport = Full("2000001", sid: 3, pir: 1);
        sport.types = new[] { "sport" };
        Seed(sport);

        var synctrackers = AppInit.conf.synctrackers;
        var syncsport = AppInit.conf.syncsport;
        try
        {
            AppInit.conf.synctrackers = new[] { "kinozal" };
            SyncCron.ApplyTorrents(Batch(_key, Stub("1113507", sid: 716, pir: 364)));
            SyncCron.ApplySpidr(Batch(_key, Stub("1113519", sid: 124, pir: 97)));

            AppInit.conf.synctrackers = null;
            AppInit.conf.syncsport = false;
            SyncCron.ApplyTorrents(Batch(_key, Stub("2000001", sid: 50, pir: 5)));
            SyncCron.ApplySpidr(Batch(_key, Stub("2000001", sid: 60, pir: 6)));
        }
        finally
        {
            AppInit.conf.synctrackers = synctrackers;
            AppInit.conf.syncsport = syncsport;
        }

        Assert.Equal(0, Record("1113507").sid);
        Assert.Equal(0, Record("1113519").sid);
        Assert.Equal(3, Record("2000001").sid);
        Assert.Equal(1, Record("2000001").pir);
    }

    [Fact]
    public void ApplySpidr_StubWithSeeders_ResetsFailedFfprobeAttempts()
    {
        // Same rule as a full update: a dead torrent that gets seeders is probed again.
        using (var fdb = FileDB.OpenWrite(_key))
        {
            fdb.Database[UrlPrefix + "1113507"].ffprobe_tryingdata = AppInit.conf.tracksatempt;
            fdb.savechanges = true;
        }
        Assert.Equal(AppInit.conf.tracksatempt, Record("1113507").ffprobe_tryingdata);

        SyncCron.ApplySpidr(Batch(_key, Stub("1113507", sid: 5, pir: 1)));

        Assert.Equal(0, Record("1113507").ffprobe_tryingdata);
    }

    [Fact]
    public void Stub_ForUnknownUrl_CreatesNothing()
    {
        var keysBefore = FileDB.masterDb.Keys.ToHashSet();

        SyncCron.ApplyTorrents(Batch(_key, Stub("9999001", sid: 10, pir: 1)));
        SyncCron.ApplySpidr(Batch(_key, Stub("9999002", sid: 10, pir: 1)));

        Assert.Equal(2, FileDB.OpenRead(_key).Count);
        Assert.False(FileDB.OpenRead(_key).ContainsKey(UrlPrefix + "9999001"));
        Assert.Equal(keysBefore, FileDB.masterDb.Keys.ToHashSet());
    }

    [Fact]
    public void Stub_ForUnknownShard_CreatesNothing()
    {
        string unknownKey = FileDB.KeyForTorrent("Нет такого шарда", "No Such Shard");
        _cleanupKeys.Add(unknownKey);
        var keysBefore = FileDB.masterDb.Keys.ToHashSet();

        SyncCron.ApplyTorrents(Batch(unknownKey, Stub("1113507", sid: 716, pir: 364)));
        SyncCron.ApplySpidr(Batch(unknownKey, Stub("1113507", sid: 716, pir: 364)));

        Assert.Equal(keysBefore, FileDB.masterDb.Keys.ToHashSet());
        Assert.False(File.Exists(FileDB.PathForKey(unknownKey)));
        // A record with the same url in another shard is not touched.
        Assert.Equal(0, Record("1113507").sid);
    }

    [Fact]
    public void ApplyTorrents_FullRecord_UpdatesAsBefore()
    {
        var full = Full("1113507", sid: 716, pir: 364);
        full.sizeName = "11 GB";

        SyncCron.ApplyTorrents(Batch(_key, full));

        var t = Record("1113507");
        Assert.Equal(716, t.sid);
        Assert.Equal(364, t.pir);
        Assert.Equal("11 GB", t.sizeName);
        Assert.Equal(2, FileDB.OpenRead(_key).Count);
    }
}
