using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Utils;
using JacRed.Models;

namespace JacRed.Application.Index
{
    public class FastDbIndex : IFastDbIndex
    {
        /// <summary>Singleton instance registered as <see cref="IFastDbIndex"/> in Program.</summary>
        public static FastDbIndex Default { get; } = new FastDbIndex();

        volatile Dictionary<string, List<string>> _fastdb;
        // The _fastdb parts ordered by their reversed text, so the parts that end
        // with a name form one run (see FranchiseKeys).
        volatile string[] _partsByEnd;
        // The same parts in ordinal order, so the parts that start with a name form one run (see TitleFormKeys).
        volatile string[] _partsByStart;
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

            var partsByEnd = new string[fastdb.Count];
            fastdb.Keys.CopyTo(partsByEnd, 0);
            Array.Sort(partsByEnd, CompareFromEnd);
            var partsByStart = new string[fastdb.Count];
            fastdb.Keys.CopyTo(partsByStart, 0);
            Array.Sort(partsByStart, StringComparer.Ordinal);

            // Keep the version captured BEFORE enumeration. A concurrent mutation
            // must remain pending, even if this build happened to include its key.
            _source = source;
            _sourceKeysVersion = version;
            _partsByEnd = partsByEnd;
            _partsByStart = partsByStart;
            _fastdb = fastdb;

            if (log)
                JacRedLog.Information("fastdb", $"rebuild end / {DateTime.Now:yyyy-MM-dd HH:mm:ss} keys={fastdb.Count}");
        }

        public void Rebuild() => Get(update: true);

        /// <summary>
        /// Bucket keys whose name or original-name part equals one of
        /// <paramref name="searchNames"/> (<c>StringConvert.SearchName</c> form).
        /// SearchName strips ':', so a torrent whose name or originalname equals
        /// one of them can only live in these buckets.
        /// </summary>
        public HashSet<string> ExactKeys(params string[] searchNames)
        {
            var fastdb = Get();
            var keys = new HashSet<string>();
            foreach (string sn in searchNames)
            {
                if (!string.IsNullOrEmpty(sn) && fastdb.TryGetValue(sn, out List<string> list))
                    keys.UnionWith(list);
            }
            return keys;
        }

        /// <summary>
        /// Shortest title, in SearchName letters, looked up behind a prefix. "Исчезновение
        /// Элеанор Ригби: Она / The Disappearance of Eleanor Rigby: Her" is another film
        /// of the same year as "Она / Her".
        /// </summary>
        public const int MinFranchiseTitle = 5;

        /// <summary>
        /// Most buckets one side of <see cref="FranchiseKeys"/> may give to an id search.
        /// A Bond title ends at most 9 parts; "огонь" or "мальчик" end about 250, and
        /// opening them on every request pushes other buckets out of evercache.
        /// </summary>
        public const int MaxFranchiseKeys = 50;

        /// <summary>
        /// Bucket keys that may hold a release named with a franchise prefix,
        /// "Джеймс Бонд 007: Голдфингер / James Bond 007: Goldfinger" for
        /// <c>голдфингер</c> or <c>goldfinger</c> (<c>StringConvert.SearchName</c> form):
        /// the name part of the key ends with <paramref name="nameSn"/> and is longer,
        /// or the original-name part does so with <paramref name="originalSn"/>.
        /// SearchName drops the ':', so <see cref="IsFranchiseMatch"/> has to confirm
        /// the records. A side gives nothing when its name is shorter than
        /// <see cref="MinFranchiseTitle"/> or more than <paramref name="max"/> buckets qualify.
        /// </summary>
        public HashSet<string> FranchiseKeys(string nameSn, string originalSn, int max)
        {
            var keys = SideKeys(nameSn, nameSide: true, max);
            keys.UnionWith(SideKeys(originalSn, nameSide: false, max));
            return keys;
        }

        HashSet<string> SideKeys(string sn, bool nameSide, int max)
        {
            var fastdb = Get();
            var parts = _partsByEnd;
            var keys = new HashSet<string>();
            if (parts == null || sn == null || sn.Length < MinFranchiseTitle)
                return keys;

            foreach (string part in PartsEndingWith(parts, sn))
            {
                if (part.Length == sn.Length || !fastdb.TryGetValue(part, out List<string> list))
                    continue;

                foreach (string key in list)
                {
                    // The part may be either half of the key; keep this side only.
                    int colon = key.IndexOf(':');
                    bool hit = nameSide
                        ? colon == part.Length && key.StartsWith(part, StringComparison.Ordinal)
                        : key.Length - colon - 1 == part.Length && key.EndsWith(part, StringComparison.Ordinal);

                    if (hit && keys.Add(key) && keys.Count > max)
                        return new HashSet<string>();
                }
            }
            return keys;
        }

        /// <summary>
        /// True when the record carries the title behind a franchise prefix, the text
        /// after a ':' in <c>StringConvert.SearchName</c> form. With both names, each side
        /// must match: whole, behind the prefix, or as the end of a name that has a prefix
        /// of its own (Alloha: "James Bond 04.5 - Never Say Never Again"). "Мертвые как я:
        /// Жизнь после смерти / Dead Like Me: Life After Death" is then not "Жизнь после
        /// смерти / After.Life". With one name, that side must match behind the prefix.
        /// </summary>
        public static bool IsFranchiseMatch(string name, string originalname, string nameSn, string originalSn)
        {
            bool hasName = !string.IsNullOrEmpty(nameSn), hasOriginal = !string.IsNullOrEmpty(originalSn);
            if (hasName && hasOriginal)
                return Math.Max(nameSn.Length, originalSn.Length) >= MinFranchiseTitle
                    && SideMatches(name, nameSn) && SideMatches(originalname, originalSn);
            if (hasName)
                return nameSn.Length >= MinFranchiseTitle && BehindPrefix(name, nameSn, tail: false);
            return hasOriginal && originalSn.Length >= MinFranchiseTitle && BehindPrefix(originalname, originalSn, tail: false);
        }

        static bool SideMatches(string title, string sn) =>
            StringConvert.SearchName(title) == sn || BehindPrefix(title, sn, tail: true);

        static bool BehindPrefix(string title, string sn, bool tail)
        {
            if (string.IsNullOrEmpty(title))
                return false;

            for (int i = title.IndexOf(':'); i >= 0; i = title.IndexOf(':', i + 1))
            {
                string rest = StringConvert.SearchName(title.Substring(i + 1));
                if (rest == null || StringConvert.SearchName(title.Substring(0, i)) == null)
                    continue;
                if (rest == sn || (tail && rest.Length >= MinFranchiseTitle && sn.EndsWith(rest, StringComparison.Ordinal)))
                    return true;
            }
            return false;
        }

        /// <summary>Most buckets <see cref="TitleFormKeys"/> may give for one name.</summary>
        public const int MaxTitleFormKeys = 100;

        /// <summary>
        /// Most parts a name may have behind it as a subtitle ("Solo Leveling: Arise from the Shadow").
        /// Any letters may follow a name, so a name with more ("Холод…" ends hundreds) gets no subtitle buckets.
        /// </summary>
        public const int MaxSubtitleParts = 10;

        /// <summary>
        /// Bucket keys that may hold a release named <paramref name="sn"/> (<c>StringConvert.SearchName</c> form)
        /// plus episodes, or, for <paramref name="season"/> &gt; 0, plus that season or a subtitle: the parts that
        /// start with the name and go on as <see cref="ReleaseTitleForms.IsCandidateRest"/> says.
        /// <see cref="ReleaseTitleForms.Matches"/> has to confirm the records. Empty when more than
        /// <see cref="MaxTitleFormKeys"/> buckets qualify.
        /// </summary>
        public HashSet<string> TitleFormKeys(string sn, int season)
        {
            var fastdb = Get();
            var parts = _partsByStart;
            var keys = new HashSet<string>();
            if (parts == null || string.IsNullOrEmpty(sn))
                return keys;

            var subtitles = new List<string>();
            foreach (string part in PartsStartingWith(parts, sn))
            {
                if (part.Length == sn.Length)
                    continue;
                string rest = part.Substring(sn.Length);
                if (ReleaseTitleForms.IsCandidateRest(rest, season))
                    keys.UnionWith(fastdb[part]);
                else if (season > 0 && sn.Length >= MinFranchiseTitle && char.IsLetter(rest[0]))
                    subtitles.Add(part);
            }

            if (subtitles.Count <= MaxSubtitleParts)
            {
                foreach (string part in subtitles)
                    keys.UnionWith(fastdb[part]);
            }

            return keys.Count > MaxTitleFormKeys ? new HashSet<string>() : keys;
        }

        static IEnumerable<string> PartsStartingWith(string[] parts, string prefix)
        {
            int i = Array.BinarySearch(parts, prefix, StringComparer.Ordinal);
            if (i < 0)
                i = ~i;
            for (; i < parts.Length && parts[i].StartsWith(prefix, StringComparison.Ordinal); i++)
                yield return parts[i];
        }

        static List<string> PartsEndingWith(string[] parts, string suffix)
        {
            // Lower bound: the first part not ordered before the suffix itself.
            int lo = 0, hi = parts.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (CompareFromEnd(parts[mid], suffix) < 0)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            var run = new List<string>();
            for (int i = lo; i < parts.Length && parts[i].EndsWith(suffix, StringComparison.Ordinal); i++)
                run.Add(parts[i]);
            return run;
        }

        /// <summary>Ordinal comparison of the reversed strings.</summary>
        static int CompareFromEnd(string x, string y)
        {
            int i = x.Length - 1, j = y.Length - 1;
            for (; i >= 0 && j >= 0; i--, j--)
            {
                if (x[i] != y[j])
                    return x[i] < y[j] ? -1 : 1;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
