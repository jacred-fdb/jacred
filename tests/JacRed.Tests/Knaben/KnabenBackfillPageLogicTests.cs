using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Knaben;
using JacRed.Models.tParse;
using Xunit;

namespace JacRed.Tests.Knaben;

public class KnabenBackfillPageLogicTests
{
    [Fact]
    public async Task FetchWithRetry_EmptyThenFull_ReturnsFullOnSecondAttempt()
    {
        int attempts = 0;
        var delays = new List<int>();

        var result = await KnabenBackfillPageLogic.FetchWithRetry(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.FromResult(new KnabenFetchPage
                    {
                        IsValid = true,
                        RawHitCount = 0,
                        TotalValue = 10000,
                        TotalRelation = "gte"
                    });
                }

                return Task.FromResult(new KnabenFetchPage
                {
                    IsValid = true,
                    RawHitCount = 300,
                    TotalValue = 10000,
                    TotalRelation = "gte"
                });
            },
            pageSize: 300,
            from: 0,
            delay: (ms, _) =>
            {
                delays.Add(ms);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(KnabenPageOutcome.Full, result.outcome);
        Assert.Equal(2, result.attempts);
        Assert.Equal(300, result.page.RawHitCount);
        Assert.Equal(new[] { 2000 }, delays);
    }

    [Fact]
    public void Classify_FullRawPage_IsFullEvenIfMappedCountWouldBeLower()
    {
        // 300 raw hits of which 280 mapped — mapped count is not an input.
        var outcome = KnabenBackfillPageLogic.Classify(
            isValid: true,
            rawHits: 300,
            pageSize: 300,
            from: 0,
            totalValue: 10000,
            totalRelation: "gte");

        Assert.Equal(KnabenPageOutcome.Full, outcome);
        Assert.Equal(
            KnabenPageOutcome.Retryable,
            KnabenBackfillPageLogic.Classify(true, 280, 300, 0, 10000, "gte"));
    }

    [Fact]
    public void Classify_EqTotal_ConfirmsEndOfFeed()
    {
        var outcome = KnabenBackfillPageLogic.Classify(
            isValid: true,
            rawHits: 171,
            pageSize: 300,
            from: 4500,
            totalValue: 4671,
            totalRelation: "eq");

        Assert.Equal(KnabenPageOutcome.EndOfFeed, outcome);
    }

    [Fact]
    public void AdvanceBackfillPass_DescWithoutOverlap_MarksPartial()
    {
        var state = new KnabenBackfillState
        {
            CategoryIndex = 12,
            CategoryId = 3005000,
            Direction = "desc",
            From = 10000,
            AscEdgeIds = new List<string> { "asc-edge" },
            DescSawOverlap = false,
            CategoryStatus = new Dictionary<string, string> { ["3005000"] = "pending" }
        };

        KnabenSyncService.AdvanceBackfillPass(state, new List<string> { "desc-only" }, earlyEnd: false);

        Assert.Equal("partial", state.CategoryStatus["3005000"]);
        Assert.Equal(3006000, state.CategoryId);
        Assert.Equal("asc", state.Direction);
        Assert.Equal(0, state.From);
        Assert.False(state.Finished);
    }
}
