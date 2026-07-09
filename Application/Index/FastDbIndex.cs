using System;
using System.Collections.Generic;
using JacRed.Engine;

namespace JacRed.Application.Index
{
    public class FastDbIndex : IFastDbIndex
    {
        /// <summary>Process-wide instance until Phase 3 DI registration.</summary>
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
                    Console.WriteLine($"fastdb: rebuild start / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

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
                    Console.WriteLine($"fastdb: rebuild end / {DateTime.Now:yyyy-MM-dd HH:mm:ss} keys={fastdb.Count}");
            }

            return _fastdb;
        }

        public void Rebuild() => Get(update: true);

        /// <summary>Backward compat shim (Phase 1A). Prefer Default.Get / Rebuild.</summary>
        public static Dictionary<string, List<string>> getFastdb(bool update = false)
            => Default.Get(update);
    }
}
