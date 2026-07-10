using JacRed.Infrastructure.Logging;
using JacRed.Models.Details;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Stats
{
    /// <summary>
    /// Lightweight JSONL.gz index for ad-hoc /stats/* torrent list queries (built during StatsCollector scan).
    /// Full index + daily shards: create/YYYY-MM-DD.jsonl.gz, update/YYYY-MM-DD.jsonl.gz.
    /// </summary>
    public static class StatsTorrentIndex
    {
        public const string IndexPath = "Data/temp/stats-torrent-index.jsonl.gz";
        public const string MetaPath = "Data/temp/stats-torrent-index-meta.json";
        public const string ShardRoot = "Data/temp/stats-torrent-index";
        public const string CreateShardDir = ShardRoot + "/create";
        public const string UpdateShardDir = ShardRoot + "/update";

        static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        });

        public sealed class Meta
        {
            public DateTime updatedAt { get; set; }
            public long entryCount { get; set; }
            /// <summary>UTC day string for create/update shards (today at collect time).</summary>
            public string shardDay { get; set; }
            public long createTodayCount { get; set; }
            public long updateTodayCount { get; set; }
        }

        public sealed class Entry
        {
            public string trackerName { get; set; }
            public string[] types { get; set; }
            public string url { get; set; }
            public string title { get; set; }
            public int sid { get; set; }
            public int pir { get; set; }
            public string sizeName { get; set; }
            public long createUtcTicks { get; set; }
            public long updateUtcTicks { get; set; }
            public bool hasMagnet { get; set; }
            public string name { get; set; }
            public string originalname { get; set; }
            public int relased { get; set; }

            public static Entry From(TorrentDetails t) => new Entry
            {
                trackerName = t.trackerName,
                types = t.types,
                url = t.url,
                title = t.title,
                sid = t.sid,
                pir = t.pir,
                sizeName = t.sizeName,
                createUtcTicks = t.createTime.ToUniversalTime().Ticks,
                updateUtcTicks = t.updateTime.ToUniversalTime().Ticks,
                hasMagnet = !string.IsNullOrEmpty(t.magnet),
                name = t.name,
                originalname = t.originalname,
                relased = t.relased
            };
        }

        public enum TorrentDayFilter
        {
            CreatedToday = 1,
            UpdatedToday = 2
        }

        public sealed class Query
        {
            public string TrackerName { get; set; }
            public TorrentDayFilter? DayFilter { get; set; }
            public int Limit { get; set; } = 200;

            public static Query ForDay(TorrentDayFilter filter, string trackerName = null, int limit = 200) =>
                new Query { DayFilter = filter, TrackerName = trackerName, Limit = limit };
        }

        public static IndexBuilder OpenBuilder(DateTime collectDayUtc) => new IndexBuilder(collectDayUtc);

        public static Meta TryLoadMeta()
        {
            try
            {
                if (!File.Exists(MetaPath))
                    return null;
                return JsonConvert.DeserializeObject<Meta>(File.ReadAllText(MetaPath));
            }
            catch
            {
                return null;
            }
        }

        public static string CreateShardPath(string day) => $"{CreateShardDir}/{day}.jsonl.gz";
        public static string UpdateShardPath(string day) => $"{UpdateShardDir}/{day}.jsonl.gz";

        public static async Task WriteResponseAsync(Stream body, Query query, CancellationToken cancellationToken = default)
        {
            var sources = ResolveSourcePaths(query);
            if (sources.Count == 0)
            {
                await body.WriteAsync(Encoding.UTF8.GetBytes("[]"), cancellationToken);
                return;
            }

            var limit = query.Limit < 1 ? 200 : Math.Min(query.Limit, 5000);
            var filterTracker = !string.IsNullOrWhiteSpace(query.TrackerName);

            var top = new TopKByCreateTime(limit);

            foreach (var path in sources)
            {
                await ScanIndexFileAsync(path, entry =>
                {
                    if (entry == null || string.IsNullOrEmpty(entry.trackerName))
                        return;

                    if (filterTracker && !string.Equals(entry.trackerName, query.TrackerName, StringComparison.OrdinalIgnoreCase))
                        return;

                    top.TryAdd(entry);
                }, cancellationToken);
            }

            await WriteJsonArrayAsync(body, top.GetSortedDescending(), cancellationToken);
        }

        static List<string> ResolveSourcePaths(Query query)
        {
            if (query.DayFilter == null)
                return new List<string>();

            var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            switch (query.DayFilter)
            {
                case TorrentDayFilter.CreatedToday:
                    {
                        var shard = CreateShardPath(today);
                        return File.Exists(shard) ? new List<string> { shard } : new List<string>();
                    }
                case TorrentDayFilter.UpdatedToday:
                    {
                        var shard = UpdateShardPath(today);
                        return File.Exists(shard) ? new List<string> { shard } : new List<string>();
                    }
                default:
                    return new List<string>();
            }
        }

        static async Task ScanIndexFileAsync(string path, Action<Entry> onEntry, CancellationToken cancellationToken)
        {
            using (var file = File.OpenRead(path))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (line.Length == 0) continue;

                    Entry entry;
                    try
                    {
                        entry = JsonConvert.DeserializeObject<Entry>(line);
                    }
                    catch
                    {
                        continue;
                    }

                    onEntry(entry);
                }
            }
        }

        static async Task WriteJsonArrayAsync(Stream body, IReadOnlyList<Entry> items, CancellationToken cancellationToken)
        {
            await body.WriteAsync(Encoding.UTF8.GetBytes("["), cancellationToken);
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    await body.WriteAsync(Encoding.UTF8.GetBytes(","), cancellationToken);

                var json = JsonConvert.SerializeObject(ToResponseDto(items[i]), Formatting.None);
                await body.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken);
            }
            await body.WriteAsync(Encoding.UTF8.GetBytes("]"), cancellationToken);
        }

        static object ToResponseDto(Entry e)
        {
            var createTime = new DateTime(e.createUtcTicks, DateTimeKind.Utc);
            var updateTime = new DateTime(e.updateUtcTicks, DateTimeKind.Utc);
            return new
            {
                e.trackerName,
                e.types,
                url = e.url,
                e.title,
                e.sid,
                e.pir,
                e.sizeName,
                createTime = createTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                updateTime = updateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                hasMagnet = e.hasMagnet,
                e.name,
                e.originalname,
                e.relased
            };
        }

        static void ResetShardDirectory(string dir)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
        }

        sealed class TopKByCreateTime
        {
            readonly int _limit;
            readonly List<Entry> _items = new List<Entry>();

            public TopKByCreateTime(int limit) => _limit = limit;

            public void TryAdd(Entry entry)
            {
                if (_items.Count < _limit)
                {
                    _items.Add(entry);
                    return;
                }

                var minIdx = 0;
                var minTicks = _items[0].createUtcTicks;
                for (int i = 1; i < _items.Count; i++)
                {
                    if (_items[i].createUtcTicks < minTicks)
                    {
                        minTicks = _items[i].createUtcTicks;
                        minIdx = i;
                    }
                }

                if (entry.createUtcTicks > minTicks)
                    _items[minIdx] = entry;
            }

            public List<Entry> GetSortedDescending()
            {
                _items.Sort((a, b) => b.createUtcTicks.CompareTo(a.createUtcTicks));
                return _items;
            }
        }

        sealed class ShardWriter : IDisposable
        {
            readonly string _finalPath;
            readonly string _tempPath;
            FileStream _fileStream;
            GZipStream _gzip;
            StreamWriter _writer;
            public long Count { get; private set; }

            public ShardWriter(string finalPath)
            {
                _finalPath = finalPath;
                _tempPath = finalPath + ".tmp";
                var dir = Path.GetDirectoryName(Path.GetFullPath(finalPath));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _fileStream = File.Create(_tempPath);
                _gzip = new GZipStream(_fileStream, CompressionLevel.Optimal);
                _writer = new StreamWriter(_gzip, new UTF8Encoding(false)) { AutoFlush = true };
            }

            public void Write(Entry entry)
            {
                Serializer.Serialize(_writer, entry);
                _writer.WriteLine();
                Count++;
            }

            public void Complete()
            {
                _writer?.Dispose();
                _gzip?.Dispose();
                _fileStream?.Dispose();
                _writer = null;
                _gzip = null;
                _fileStream = null;

                if (File.Exists(_finalPath))
                    File.Replace(_tempPath, _finalPath, null);
                else
                    File.Move(_tempPath, _finalPath);
            }

            public void Dispose()
            {
                try { _writer?.Dispose(); } catch { }
                try { _gzip?.Dispose(); } catch { }
                try { _fileStream?.Dispose(); } catch { }

                if (File.Exists(_tempPath))
                {
                    try { File.Delete(_tempPath); } catch { }
                }
            }
        }

        public sealed class IndexBuilder : IDisposable
        {
            readonly string _tempPath;
            readonly string _shardDay;
            readonly long _todayTicks;
            ShardWriter _createTodayShard;
            ShardWriter _updateTodayShard;
            long _createTodayCount;
            long _updateTodayCount;
            FileStream _fileStream;
            GZipStream _gzip;
            StreamWriter _writer;
            long _count;

            public IndexBuilder(DateTime collectDayUtc)
            {
                _shardDay = collectDayUtc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                _todayTicks = collectDayUtc.Date.Ticks;

                ResetShardDirectory(CreateShardDir);
                ResetShardDirectory(UpdateShardDir);

                _tempPath = IndexPath + ".tmp";
                var dir = Path.GetDirectoryName(Path.GetFullPath(IndexPath));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _fileStream = File.Create(_tempPath);
                _gzip = new GZipStream(_fileStream, CompressionLevel.Optimal);
                _writer = new StreamWriter(_gzip, new UTF8Encoding(false)) { AutoFlush = true };
            }

            public void Write(TorrentDetails torrent)
            {
                if (torrent == null || string.IsNullOrEmpty(torrent.trackerName))
                    return;

                var entry = Entry.From(torrent);
                Serializer.Serialize(_writer, entry);
                _writer.WriteLine();
                _count++;

                if (entry.createUtcTicks >= _todayTicks)
                {
                    _createTodayShard ??= new ShardWriter(CreateShardPath(_shardDay));
                    _createTodayShard.Write(entry);
                    _createTodayCount++;
                }

                if (entry.updateUtcTicks >= _todayTicks)
                {
                    _updateTodayShard ??= new ShardWriter(UpdateShardPath(_shardDay));
                    _updateTodayShard.Write(entry);
                    _updateTodayCount++;
                }
            }

            public void Complete(DateTime updatedAtUtc)
            {
                _writer?.Dispose();
                _gzip?.Dispose();
                _fileStream?.Dispose();
                _writer = null;
                _gzip = null;
                _fileStream = null;

                if (File.Exists(IndexPath))
                    File.Replace(_tempPath, IndexPath, null);
                else
                    File.Move(_tempPath, IndexPath);

                _createTodayShard?.Complete();
                _updateTodayShard?.Complete();

                var meta = new Meta
                {
                    updatedAt = updatedAtUtc,
                    entryCount = _count,
                    shardDay = _shardDay,
                    createTodayCount = _createTodayCount,
                    updateTodayCount = _updateTodayCount
                };
                StatsCollector.WriteTextAtomic(MetaPath, JsonConvert.SerializeObject(meta, Formatting.Indented));

                JacRedLog.Information(JacRedLogCategories.Stats,
                    $"torrent index {_count} entries, today create={_createTodayCount} update={_updateTodayCount} ({_shardDay}) / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }

            public void Dispose()
            {
                try { _writer?.Dispose(); } catch { }
                try { _gzip?.Dispose(); } catch { }
                try { _fileStream?.Dispose(); } catch { }

                _createTodayShard?.Dispose();
                _updateTodayShard?.Dispose();

                if (File.Exists(_tempPath))
                {
                    try { File.Delete(_tempPath); } catch { }
                }
            }
        }
    }
}
