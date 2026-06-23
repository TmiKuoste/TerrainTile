using Kuoste.TerrainEngine.Common.Tiles;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.Common.Tests.Tiles;

public class TileTests
{
    [Fact]
    public void CompletedRequired_IsThree()
    {
        var tile = new Tile();
        Assert.Equal(3, tile.CompletedRequired);
    }

    [Fact]
    public void IsCompleted_WhenCompletedCountBelowRequired_ReturnsFalse()
    {
        var tile = new Tile();
        tile.CompletedCount = 2;
        Assert.False(tile.IsCompleted);
    }

    [Fact]
    public void IsCompleted_WhenCompletedCountEqualsRequired_ReturnsTrue()
    {
        var tile = new Tile();
        Interlocked.Exchange(ref tile.CompletedCount, 3);
        Assert.True(tile.IsCompleted);
    }

    [Fact]
    public void IsCompleted_WhenCompletedCountExceedsRequired_ReturnsTrue()
    {
        var tile = new Tile();
        Interlocked.Exchange(ref tile.CompletedCount, 5);
        Assert.True(tile.IsCompleted);
    }

    [Fact]
    public void Clear_EmptiesCollectionsAndNullsData()
    {
        var tile = new Tile();
        tile.Buildings.Add(default(Tile.Building));

        tile.Clear();

        Assert.Empty(tile.Buildings);
        Assert.Empty(tile.Trees);
        Assert.Empty(tile.WaterAreas);
        Assert.Null(tile.DemDsm);
        Assert.Null(tile.BuildingsRoads);
        Assert.Null(tile.TerrainType);
    }
}
