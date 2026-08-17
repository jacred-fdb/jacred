using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Models.Details;
using JacRed.Models.tParse;

namespace JacRed.Infrastructure.Trackers.Knaben
{
    internal enum KnabenPageOutcome
    {
        Full,
        EndOfFeed,
        Retryable
    }

    internal sealed class KnabenFetchPage
    {
        public bool IsValid { get; set; }
        public int RawHitCount { get; set; }
        public int? TotalValue { get; set; }
        public string TotalRelation { get; set; }
        public List<TorrentDetails> Torrents { get; set; } = new List<TorrentDetails>();
        public List<string> Ids { get; set; } = new List<string>();

        public static KnabenFetchPage Invalid() => new KnabenFetchPage { IsValid = false };

        public static KnabenFetchPage FromResponse(KnabenApiResponse resp)
        {
            if (resp?.Hits == null)
                return Invalid();

            var ids = resp.Hits
                .Select(h => h.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
            var torrents = resp.Hits.Select(KnabenParser.MapToTorrentDetails).Where(t => t != null).ToList();
            return new KnabenFetchPage
            {
                IsValid = true,
                RawHitCount = resp.Hits.Count,
                TotalValue = resp.Total?.Value,
                TotalRelation = resp.Total?.Relation,
                Torrents = torrents,
                Ids = ids
            };
        }
    }

    /// <summary>
    /// Classifies Knaben API pages for archive backfill.
    /// End-of-feed uses raw hit count and total.relation=eq, not mapped torrent count.
    /// </summary>
    internal static class KnabenBackfillPageLogic
    {
        public const int MaxAttempts = 3;
        public static readonly int[] RetryBackoffMs = { 2000, 8000 };

        public static KnabenPageOutcome Classify(
            bool isValid,
            int rawHits,
            int pageSize,
            int from,
            int? totalValue,
            string totalRelation)
        {
            if (!isValid)
                return KnabenPageOutcome.Retryable;

            if (rawHits == pageSize)
                return KnabenPageOutcome.Full;

            if (string.Equals(totalRelation, "eq", StringComparison.OrdinalIgnoreCase)
                && totalValue.HasValue
                && from + rawHits >= totalValue.Value)
                return KnabenPageOutcome.EndOfFeed;

            return KnabenPageOutcome.Retryable;
        }

        public static KnabenPageOutcome Classify(KnabenFetchPage page, int pageSize, int from)
        {
            if (page == null)
                return KnabenPageOutcome.Retryable;
            return Classify(page.IsValid, page.RawHitCount, pageSize, from, page.TotalValue, page.TotalRelation);
        }

        public static async Task<(KnabenFetchPage page, KnabenPageOutcome outcome, int attempts)> FetchWithRetry(
            Func<CancellationToken, Task<KnabenFetchPage>> fetch,
            int pageSize,
            int from,
            Func<int, CancellationToken, Task> delay,
            CancellationToken cancellationToken,
            int maxAttempts = MaxAttempts,
            Action<KnabenFetchPage, KnabenPageOutcome, int> onAttempt = null)
        {
            KnabenFetchPage last = KnabenFetchPage.Invalid();
            var outcome = KnabenPageOutcome.Retryable;
            int attempts = 0;
            int retries = Math.Max(1, maxAttempts);

            for (int i = 0; i < retries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                last = await fetch(cancellationToken) ?? KnabenFetchPage.Invalid();
                attempts++;
                outcome = Classify(last, pageSize, from);
                onAttempt?.Invoke(last, outcome, attempts);
                if (outcome != KnabenPageOutcome.Retryable)
                    return (last, outcome, attempts);
                if (i < retries - 1)
                {
                    int backoff = RetryBackoffMs[Math.Min(i, RetryBackoffMs.Length - 1)];
                    await delay(backoff, cancellationToken);
                }
            }

            return (last, outcome, attempts);
        }
    }
}
