using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;
using JacRed.Models;

namespace JacRed.Application.Index
{
    public class FastDbIndex : IFastDbIndex
    {
        /// <summary>Singleton instance registered as <see cref="IFastDbIndex"/> in Program.</summary>
        public static FastDbIndex Default { get; } = new FastDbIndex();

        volatile Dictionary<string, List<string>> _fastdb;
        ConcurrentDictionary<string, MasterDbShard> _source;
        long _sourceKeysVersion;
        readonly object _lock = new object();

        public Dictionary<string, List<string>> Get(bool update = false)
        {
            if (_fastdb != null && !update)
                return _fastdb;

            lock (_lock)
            {
                if (_fastdb != null && !update)
                    return _fastdb;

                Build(FileDB.masterDb, FileDB.SearchKeysVersion, update);
            }

            return _fastdb;
        }

        public bool RebuildIfChanged()
        {
            lock (_lock)
            {
                var source = FileDB.masterDb;
                long version = FileDB.SearchKeysVersion;
                if (_fastdb != null && ReferenceEquals(_source, source) && _sourceKeysVersion == version)
                    return false;

                Build(source, version, log: true);
                return true;
            }
        }

        // Called under _lock. Publish only a complete index; readers keep using
        // the previous one during a rebuild, including when a build fails.
        void Build(ConcurrentDictionary<string, MasterDbShard> source, long version, bool log)
        {
            if (log)
                JacRedLog.Information("fastdb", $"rebuild start / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            var fastdb = new Dictionary<string, List<string>>();

            // ConcurrentDictionary enumeration does not allocate the full shard
            // snapshot. Changes during enumeration invalidate the next refresh.
            foreach (var item in source)
            {
                foreach (string k in item.Key.Split(":"))
                {
                    if (string.IsNullOrEmpty(k))
                        continue;

                    if (fastdb.TryGetValue(k, out List<string> keys))
                        keys.Add(item.Key);
                    else
                        fastdb.Add(k, new List<string>() { item.Key });
                }
            }

            // Keep the version captured BEFORE enumeration. A concurrent mutation
            // must remain pending, even if this build happened to include its key.
            _source = source;
            _sourceKeysVersion = version;
            _fastdb = fastdb;

            if (log)
                JacRedLog.Information("fastdb", $"rebuild end / {DateTime.Now:yyyy-MM-dd HH:mm:ss} keys={fastdb.Count}");
        }

        public void Rebuild() => Get(update: true);
    }
}
