using JacRed.Infrastructure.Indexers;
using Xunit;

namespace JacRed.Tests.Indexers;

public class NumQueryParserTests
{
    [Fact]
    public void Parse_RuEnYear_ExtractsTitleOriginalAndYear()
    {
        var p = NumQueryParser.Parse("Криминальное чтиво Pulp Fiction 1994");

        Assert.True(p.Matched);
        Assert.Equal("Криминальное чтиво", p.Title);
        Assert.Equal("Pulp Fiction", p.TitleOriginal);
        Assert.Equal(1994, p.Year);
    }

    [Fact]
    public void Parse_EnRuYear_LampaDfLgYear()
    {
        var p = NumQueryParser.Parse("Pulp Fiction Криминальное чтиво 1994");

        Assert.True(p.Matched);
        Assert.Equal("Криминальное чтиво", p.Title);
        Assert.Equal("Pulp Fiction", p.TitleOriginal);
        Assert.Equal(1994, p.Year);
    }

    [Fact]
    public void Parse_EnRu_LampaDfLg()
    {
        var p = NumQueryParser.Parse("Pulp Fiction Криминальное чтиво");

        Assert.True(p.Matched);
        Assert.Equal("Криминальное чтиво", p.Title);
        Assert.Equal("Pulp Fiction", p.TitleOriginal);
        Assert.Equal(0, p.Year);
    }

    [Fact]
    public void Parse_OriginalTrailingYear_SetsTitleOriginalAndYear()
    {
        var p = NumQueryParser.Parse("Pulp Fiction 1994");

        Assert.True(p.Matched);
        Assert.True(string.IsNullOrEmpty(p.Title));
        Assert.Equal("Pulp Fiction", p.TitleOriginal);
        Assert.Equal(1994, p.Year);
    }

    [Fact]
    public void Parse_RuEn_ExtractsTitles()
    {
        var p = NumQueryParser.Parse("Константин Constantine");

        Assert.True(p.Matched);
        Assert.Equal("Константин", p.Title);
        Assert.Equal("Constantine", p.TitleOriginal);
        Assert.Equal(0, p.Year);
    }

    [Fact]
    public void Parse_RuEnYear_Constantine()
    {
        var p = NumQueryParser.Parse("Константин Constantine 2005");

        Assert.True(p.Matched);
        Assert.Equal("Константин", p.Title);
        Assert.Equal("Constantine", p.TitleOriginal);
        Assert.Equal(2005, p.Year);
    }

    [Fact]
    public void Parse_CyrillicTrailingYear_DoesNotEmpty_SetsTitleAndYear()
    {
        var p = NumQueryParser.Parse("Константин 2005");

        Assert.True(p.Matched);
        Assert.Equal("Константин", p.Title);
        Assert.True(string.IsNullOrEmpty(p.TitleOriginal));
        Assert.Equal(2005, p.Year);
    }

    [Fact]
    public void Parse_CyrillicOnly_SetsTitle()
    {
        var p = NumQueryParser.Parse("Криминальное чтиво");

        Assert.True(p.Matched);
        Assert.Equal("Криминальное чтиво", p.Title);
        Assert.Equal(0, p.Year);
    }

    [Fact]
    public void ApplyToRequest_RqNum_PromotesQueryToCardMode()
    {
        var req = new IndexerSearchRequest
        {
            Query = "Криминальное чтиво Pulp Fiction 1994",
            RqNum = true,
            IsSerial = -1
        };

        Assert.True(NumQueryParser.ApplyToRequest(req));
        Assert.True(req.CardMode);
        Assert.Equal("Криминальное чтиво", req.Title);
        Assert.Equal("Pulp Fiction", req.TitleOriginal);
        Assert.Equal(1994, req.Year);
    }

    [Fact]
    public void ApplyToRequest_WithoutRqNum_PromotesWhenTitlesAbsent()
    {
        // Lampa clarification: Query only, no title params (RqNum false due to is_serial).
        var req = new IndexerSearchRequest
        {
            Query = "Pulp Fiction Криминальное чтиво 1994",
            RqNum = false,
            IsSerial = 1
        };

        Assert.True(NumQueryParser.ApplyToRequest(req));
        Assert.True(req.CardMode);
        Assert.Equal("Криминальное чтиво", req.Title);
        Assert.Equal("Pulp Fiction", req.TitleOriginal);
        Assert.Equal(1994, req.Year);
    }

    [Fact]
    public void ApplyToRequest_CyrillicYear_EnablesCardMode()
    {
        var req = new IndexerSearchRequest
        {
            Query = "Константин 2005",
            RqNum = true
        };

        Assert.True(NumQueryParser.ApplyToRequest(req));
        Assert.True(req.CardMode);
        Assert.Equal("Константин", req.Title);
        Assert.Equal(2005, req.Year);
    }

    [Fact]
    public void ApplyToRequest_SkipsWhenTitleAlreadySet()
    {
        var req = new IndexerSearchRequest
        {
            Query = "Криминальное чтиво Pulp Fiction 1994",
            Title = "Already",
            RqNum = true
        };

        Assert.False(NumQueryParser.ApplyToRequest(req));
        Assert.Equal("Already", req.Title);
    }

    [Fact]
    public void Prowlarr_PlainRuEnYear_StillEnriches()
    {
        var parsed = ProwlarrQueryParser.Parse("Криминальное чтиво Pulp Fiction 1994", "search");

        Assert.Equal("Криминальное чтиво", parsed.Title);
        Assert.Equal("Pulp Fiction", parsed.TitleOriginal);
        Assert.Equal(1994, parsed.Year);
    }

    [Fact]
    public void Prowlarr_PlainEnRuYear_StillEnriches()
    {
        var parsed = ProwlarrQueryParser.Parse("Pulp Fiction Криминальное чтиво 1994", "search");

        Assert.Equal("Криминальное чтиво", parsed.Title);
        Assert.Equal("Pulp Fiction", parsed.TitleOriginal);
        Assert.Equal(1994, parsed.Year);
    }
}
