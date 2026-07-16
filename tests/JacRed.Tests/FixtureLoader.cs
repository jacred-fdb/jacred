using System.IO;
using Xunit;

namespace JacRed.Tests;

static class FixtureLoader
{
    public static string Read(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath);
        Assert.True(File.Exists(path), $"Fixture missing: {path}");
        return File.ReadAllText(path);
    }
}
