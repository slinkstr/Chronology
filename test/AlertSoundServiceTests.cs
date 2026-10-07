using Chronology.Services;
using Xunit;

namespace Chronology.Tests;

public class AlertSoundServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PlayAsync_InvalidCount_Throws(int count)
    {
        var service = new AlertSoundService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.PlayAsync(null, count, CancellationToken.None));
    }
}
