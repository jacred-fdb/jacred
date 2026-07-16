using System.IO;
using Xunit;

namespace JacRed.Tests;

static class FixtureLoader
{
    public static string Read(string relativePath)
    {
        Assert.False(Path.IsPathRooted(relativePath), $"Fixture path must be relative: {relativePath}");
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath);
        Assert.True(File.Exists(path), $"Fixture missing: {path}");
        return File.ReadAllText(path);
    }
}
