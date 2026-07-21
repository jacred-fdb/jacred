using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Tracks;
using Xunit;

namespace JacRed.Tests.Tracks;

public class TracksAnalyzerHashLockTests
{
    [Fact]
    public async Task TryAcquireHashLockAsync_blocks_second_caller()
    {
        const string hash = "aabbccddeeff00112233445566778899aabbccdd";

        using (var first = await TracksAnalyzer.TryAcquireHashLockAsync(hash, CancellationToken.None))
        {
            Assert.NotNull(first);

            var second = await TracksAnalyzer.TryAcquireHashLockAsync(hash, CancellationToken.None);
            Assert.Null(second);
        }

        using (var third = await TracksAnalyzer.TryAcquireHashLockAsync(hash, CancellationToken.None))
        {
            Assert.NotNull(third);
        }
    }
}
