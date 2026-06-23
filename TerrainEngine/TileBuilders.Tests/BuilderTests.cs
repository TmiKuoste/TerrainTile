using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests;

public class BuilderTests
{
    private sealed class ConcreteBuilder : Builder { }

    [Fact]
    public void IsCancellationRequested_WithDefaultToken_ReturnsFalse()
    {
        var builder = new ConcreteBuilder { CancellationToken = default, Logger = NullLogger.Instance };
        Assert.False(builder.IsCancellationRequested());
    }

    [Fact]
    public void IsCancellationRequested_WithNonCancelledToken_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource();
        var builder = new ConcreteBuilder { CancellationToken = cts.Token, Logger = NullLogger.Instance };
        Assert.False(builder.IsCancellationRequested());
    }

    [Fact]
    public void IsCancellationRequested_WithCancelledToken_ReturnsTrue()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var builder = new ConcreteBuilder { CancellationToken = cts.Token, Logger = NullLogger.Instance };
        Assert.True(builder.IsCancellationRequested());
    }
}
