using System;
using System.Linq;
using System.Reflection;
using JacRed.Infrastructure.Trackers;
using Xunit;

namespace JacRed.Tests.Trackers;

public class ParseAllStarterCoverageTests
{
    /// <summary>Trio ParseAll slugs in Data/crontab — resume must include exactly these.</summary>
    static readonly string[] ExpectedParseAllSlugs =
    [
        "anibelka",
        "kinozal",
        "korsars",
        "megapeer",
        "nnmclub",
        "rutor",
        "rutracker",
        "toloka",
        "torrentby",
        "ultradox"
    ];

    [Fact]
    public void IParseAllStarterTypes_MatchCrontabParseAllTrackers()
    {
        var types = typeof(IParseAllStarter).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IParseAllStarter).IsAssignableFrom(t))
            .ToArray();

        var names = types
            .Select(GetTrackerNameConst)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(
            ExpectedParseAllSlugs.OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
            names);
    }

    [Fact]
    public void EachStarterHasMatchingCycleAndTaskParsePaths()
    {
        foreach (var slug in ExpectedParseAllSlugs)
        {
            Assert.Equal($"Data/temp/{slug}_parseAllCycle.json", ParseAllCycleStore.CyclePathForTracker(slug));
            Assert.Equal($"Data/temp/{slug}_taskParse.json", ParseAllCycleStore.TaskParsePathForTracker(slug));
        }
    }

    static string GetTrackerNameConst(Type type)
    {
        var field = type.GetField("TrackerName", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert.True(field != null && field.FieldType == typeof(string), $"{type.Name} is missing const TrackerName");
        var name = (string)field.GetValue(null);
        Assert.False(string.IsNullOrWhiteSpace(name), $"{type.Name}.TrackerName is empty");
        return name;
    }
}
