using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace JacRed.Tests.Indexers;

// q=Title S02 without season= (AIOStreams forced query search)
public class QuerySeasonPostFilterTests
{
    const string FirstSeason = "Dan Da Dan | Дандадан [2024, TV, 12 из 12] WEBRip 720p raw";
    const string SecondSeason = "Дандадан (2 сезон: 1-12 серии из 12) / Dandadan / 2025 / ДБ / WEBRip (1080p)";
    const string NamedOtherSeason = "Дандадан (ТВ-1) / Dan Da Dan / Dandadan [TV] [12 из 12] [2024, BDRemux] [1080p]";
    const string Absolute = "[SubsPlease] Dandadan - 16 (1080p)";

    static List<string> Run(Dictionary<string, StringValues> args)
    {
        var query = new QueryCollection(args);
        var req = IndexerSearchHelper.BuildRequest(query, apikey: null, rqnum: false);
        var results = new[] { FirstSeason, SecondSeason, NamedOtherSeason, Absolute }.Select(t => new Result { Title = t }).ToList();
        return IndexerSearchHelper.ApplyPostFilters(results, query, req, "search").Select(r => r.Title).ToList();
    }

    [Fact]
    public void SeasonOnlyInText_DropsFirstSeasonGuessOnly()
    {
        var titles = Run(new Dictionary<string, StringValues> { ["q"] = "dan da dan S02E04" });
        Assert.Equal(new[] { SecondSeason, NamedOtherSeason, Absolute }, titles);
    }

    [Fact]
    public void SeasonParam_StaysStrict()
    {
        var titles = Run(new Dictionary<string, StringValues> { ["q"] = "dan da dan", ["season"] = "2", ["ep"] = "4" });
        Assert.Equal(new[] { SecondSeason }, titles);
    }

    [Fact]
    public void NoSeason_NoFilter()
    {
        var titles = Run(new Dictionary<string, StringValues> { ["q"] = "dan da dan" });
        Assert.Equal(4, titles.Count);
    }
}
