using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Models.Details;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.Bitru
{
    internal readonly struct BitruBackfillPage
    {
        public BitruBackfillPage(IReadOnlyList<TorrentDetails> torrents, long? nextCursor, bool stop, HashSet<long> ids = null)
        {
            Torrents = torrents ?? Array.Empty<TorrentDetails>();
            NextCursor = nextCursor;
            Stop = stop;
            Ids = ids;
        }

        public IReadOnlyList<TorrentDetails> Torrents { get; }
        public long? NextCursor { get; }
        public bool Stop { get; }
        public HashSet<long> Ids { get; }

        public static BitruBackfillPage Halt() => new(Array.Empty<TorrentDetails>(), null, stop: true);

        public static BitruBackfillPage Ok(IReadOnlyList<TorrentDetails> torrents, long? nextCursor, HashSet<long> ids)
            => new(torrents, nextCursor, stop: false, ids);
    }

    internal sealed class BitruBackfillProgress
    {
        public int FetchedPages { get; set; }
        public int CommittedPages { get; set; }
        public int SavedCount { get; set; }
        public long? LastCommittedCursor { get; set; }

        public string FormatLog()
        {
            string cursor = FormatCursor(LastCommittedCursor);
            if (SavedCount == 0 && CommittedPages == 0)
                return $"no items, fetchedPages={FetchedPages}, committedPages={CommittedPages}, cursor={cursor}";
            return $"saved {SavedCount}, fetchedPages={FetchedPages}, committedPages={CommittedPages}, cursor={cursor}";
        }

        public string FormatCanceledLog()
        {
            string cursor = FormatCursor(LastCommittedCursor);
            return $"canceled, saved={SavedCount}, fetchedPages={FetchedPages}, committedPages={CommittedPages}, cursor={cursor}";
        }

        static string FormatCursor(long? cursor)
            => cursor?.ToString(CultureInfo.InvariantCulture) ?? "none";
    }

    /// <summary>
    /// Page-by-page backfill: fetch → save → commit cursor.
    /// Cursor advances only after the page is fully saved.
    /// </summary>
    internal static class BitruBackfillCommitLoop
    {
        public static async Task<BitruBackfillProgress> RunAsync(
            int maxPages,
            long? startCursor,
            Func<long?, CancellationToken, Task<BitruBackfillPage>> fetchPage,
            Func<IReadOnlyList<TorrentDetails>, CancellationToken, Task> savePage,
            Action<long> commitCursor,
            BitruBackfillProgress progress,
            CancellationToken cancellationToken)
        {
            if (!progress.LastCommittedCursor.HasValue && startCursor.HasValue)
                progress.LastCommittedCursor = startCursor;

            maxPages = BitruApiPagination.ClampPages(maxPages);
            long? requestCursor = startCursor;

            for (int page = 0; page < maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fetched = await fetchPage(requestCursor, cancellationToken);
                if (fetched.Stop)
                    break;

                progress.FetchedPages++;

                await savePage(fetched.Torrents, cancellationToken);

                progress.SavedCount += fetched.Torrents.Count;
                progress.CommittedPages++;

                if (!fetched.NextCursor.HasValue)
                    break;

                commitCursor(fetched.NextCursor.Value);
                progress.LastCommittedCursor = fetched.NextCursor;
                requestCursor = fetched.NextCursor;
            }

            return progress;
        }

        public static void WriteCursorAtomic(string path, long unix)
        {
            var fullPath = IO.Path.GetFullPath(path);
            var dir = IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !IO.Directory.Exists(dir))
                IO.Directory.CreateDirectory(dir);

            string text = unix.ToString(CultureInfo.InvariantCulture);
            string tempPath = fullPath + ".tmp";
            IO.File.WriteAllText(tempPath, text);
            if (IO.File.Exists(fullPath))
                IO.File.Replace(tempPath, fullPath, null);
            else
                IO.File.Move(tempPath, fullPath);
        }

        public static long? ReadCursor(string path)
        {
            if (!IO.File.Exists(path))
                return null;
            string text = IO.File.ReadAllText(path).Trim();
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unix) && unix > 0)
                return unix;
            return null;
        }
    }
}
