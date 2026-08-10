using System.Collections.Generic;
using JacRed.Infrastructure.Trackers.Bitru;
using JacRed.Models.tParse;
using Xunit;

namespace JacRed.Tests.Bitru;

public class BitruApiPaginationTests
{
    [Fact]
    public void BuildRequestParams_WithoutCursor_HasNoDateFilter()
    {
        var p = BitruApiPagination.BuildRequestParams(100, null);
        Assert.Equal(100, p["limit"]);
        Assert.Equal(BitruCategories.RequestCategories, p["category"]);
        Assert.False(p.ContainsKey("after_date"));
        Assert.False(p.ContainsKey("before_date"));
    }

    [Fact]
    public void BuildRequestParams_OlderPage_UsesAfterDateNotBeforeDate()
    {
        var p = BitruApiPagination.BuildRequestParams(50, 1786263023);
        Assert.Equal("1786263023", p[BitruApiPagination.AfterDateParam]);
        Assert.False(p.ContainsKey("before_date"));
    }

    [Fact]
    public void TryGetNextOlderPageCursor_UsesBeforeDateAsNextAfterDate()
    {
        var result = new BitruApiResult { BeforeDate = 1786263023L, AfterDate = 1786278241L };
        Assert.True(BitruApiPagination.TryGetNextOlderPageCursor(result, previousCursor: null, out long next));
        Assert.Equal(1786263023L, next);
    }

    [Fact]
    public void TryGetNextOlderPageCursor_StopsWhenUnchanged()
    {
        var result = new BitruApiResult { BeforeDate = "100" };
        Assert.False(BitruApiPagination.TryGetNextOlderPageCursor(result, previousCursor: 100, out _));
    }

    [Fact]
    public void TryGetNextOlderPageCursor_MissingOrZero_Fails()
    {
        Assert.False(BitruApiPagination.TryGetNextOlderPageCursor(null, null, out _));
        Assert.False(BitruApiPagination.TryGetNextOlderPageCursor(new BitruApiResult(), null, out _));
        Assert.False(BitruApiPagination.TryGetNextOlderPageCursor(new BitruApiResult { BeforeDate = 0 }, null, out _));
    }

    [Fact]
    public void IsDuplicatePage_TrueWhenCurrentFullyContained()
    {
        var prev = new HashSet<long> { 1, 2, 3, 4, 5 };
        var curr = new List<long> { 2, 4, 5 };
        Assert.True(BitruApiPagination.IsDuplicatePage(prev, curr));
    }

    [Fact]
    public void IsDuplicatePage_FalseWhenNewIdsPresent()
    {
        var prev = new HashSet<long> { 1, 2, 3 };
        var curr = new List<long> { 3, 4, 5 };
        Assert.False(BitruApiPagination.IsDuplicatePage(prev, curr));
    }

    [Fact]
    public void TryExtractTorrentId_FromDetailsUrl()
    {
        Assert.True(BitruApiPagination.TryExtractTorrentId("https://bitru.org/details.php?id=729321", out long id));
        Assert.Equal(729321L, id);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    [InlineData(100, 50)]
    public void ClampPages_RespectsHardLimit(int input, int expected)
    {
        Assert.Equal(expected, BitruApiPagination.ClampPages(input));
    }
}
