using System;
using System.Collections.Generic;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;

namespace JacRed.Application.Index
{
    public class FastDbIndex : IFastDbIndex
    {
        /// <summary>Singleton instance registered as <see cref="IFastDbIndex"/> in Program.</summary>
        public static FastDbIndex Default { get; } = new FastDbIndex();

        Dictionary<string, List<string>> _fastdb;
        readonly object _lock = new object();

        public Dictionary<string, List<string>> Get(bool update = false)
        {
            if (_fastdb != null && !update)
                return _fastdb;

            lock (_lock)
            {
                if (_fastdb != null && !update)
                    return _fastdb;

                if (update)
                    JacRedLog.Information("fastdb", $"rebuild start / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                var fastdb = new Dictionary<string, List<string>>();

                foreach (var item in FileDB.masterDb.ToArray())
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

                _fastdb = fastdb;

                if (update)
                    JacRedLog.Information("fastdb", $"rebuild end / {DateTime.Now:yyyy-MM-dd HH:mm:ss} keys={fastdb.Count}");
            }

            return _fastdb;
        }

        public void Rebuild() => Get(update: true);

        /// <summary>
        /// Resolve masterDb shard keys via fastdb tokens instead of scanning every masterDb key.
        /// exact: token equals sn/altSn; fuzzy: token Contains sn/altSn.
        /// </summary>
        public System.Collections.Generic.IEnumerable<string> LookupMasterKeys(string sn, string altSn, bool exact, int? take = null)
        {
            var fastdb = Get();
            if (fastdb == null || fastdb.Count == 0)
                return System.Linq.Enumerable.Empty<string>();

            var keys = new HashSet<string>(StringComparer.Ordinal);

            void AddFromToken(string token)
            {
                if (string.IsNullOrEmpty(token))
                    return;
                if (fastdb.TryGetValue(token, out List<string> list))
                {
                    foreach (var k in list)
                        keys.Add(k);
                }
            }

            if (exact)
            {
                AddFromToken(sn);
                AddFromToken(altSn);
            }
            else
            {
                foreach (var kv in fastdb)
                {
                    if ((sn != null && kv.Key.Contains(sn, StringComparison.Ordinal))
                        || (altSn != null && kv.Key.Contains(altSn, StringComparison.Ordinal)))
                    {
                        foreach (var k in kv.Value)
                            keys.Add(k);
                    }
                }
            }

            if (take.HasValue && take.Value > 0 && keys.Count > take.Value)
                return System.Linq.Enumerable.Take(keys, take.Value);

            return keys;
        }
    }
}
