using System;
using System.Linq;
using JacRed.Application.Search;
using JacRed.Configuration.Schema;
using Xunit;

namespace JacRed.Tests.Indexers;

public class TrackerCatalogServiceTests
{
    [Fact]
    public async Task GetTrackerNames_WhenSynctrackersNull_ReturnsKnownMinusDisabled()
    {
        await WithTrackers(null, new[] { "rutor", "KINOZAL" }, async () =>
        {
            var service = new TrackerCatalogService();
            var names = await service.GetTrackerNamesAsync();

            Assert.DoesNotContain("rutor", names, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("kinozal", names, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("rutracker", names);
            Assert.True(names.SequenceEqual(names.OrderBy(i => i, StringComparer.OrdinalIgnoreCase)));
            Assert.True(names.All(n => ConfigSchema.KnownTrackerSlugs.Contains(n)));
        });
    }

    [Fact]
    public async Task GetTrackerNames_WhenSynctrackersSet_UsesConfigList()
    {
        await WithTrackers(new[] { "rutracker", "kinozal", "rutracker" }, new[] { "kinozal" }, async () =>
        {
            var service = new TrackerCatalogService();
            var names = await service.GetTrackerNamesAsync();

            Assert.Equal(new[] { "rutracker" }, names);
        });
    }

    [Fact]
    public async Task GetTrackerNames_WhenSynctrackersEmpty_ReturnsEmpty()
    {
        await WithTrackers(Array.Empty<string>(), null, async () =>
        {
            var service = new TrackerCatalogService();
            var names = await service.GetTrackerNamesAsync();

            Assert.Empty(names);
        });
    }

    static async Task WithTrackers(string[] synctrackers, string[] disableTrackers, Func<Task> action)
    {
        _ = AppInit.conf;
        var prevSync = AppInit.conf.synctrackers;
        var prevDisable = AppInit.conf.disable_trackers;
        try
        {
            AppInit.conf.synctrackers = synctrackers;
            AppInit.conf.disable_trackers = disableTrackers ?? Array.Empty<string>();
            await action();
        }
        finally
        {
            AppInit.conf.synctrackers = prevSync;
            AppInit.conf.disable_trackers = prevDisable;
        }
    }
}
