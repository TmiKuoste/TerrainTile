using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using Kuoste.TerrainEngine.TileBuilders.WaterAreas;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.WaterAreas;

public class WaterAreasReaderTests : IDisposable
{
    private const string TileName = "L4133B3_4";
    private const string Version = "1";

    // A closed polygon with 5 coordinate entries (first = last)
    private const string OnePolygon =
        "Polygon " +
        "[384100,6672100,0] [384200,6672100,0] [384200,6672200,0] [384100,6672200,0] [384100,6672100,0]";

    private readonly string _tempDir;

    public WaterAreasReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TerrainTileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Build_WhenCancelled_ReturnsEmptyList()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = MakeReader(cts.Token).Build(MakeTile());

        Assert.Empty(result);
    }

    [Fact]
    public void Build_WithOnePolygon_ReturnsSinglePolygon()
    {
        WriteFixture(OnePolygon);

        var result = MakeReader(CancellationToken.None).Build(MakeTile());

        Assert.Single(result);
    }

    [Fact]
    public void Build_WithOnePolygon_HasExpectedCoordinateCount()
    {
        WriteFixture(OnePolygon);

        var polygon = MakeReader(CancellationToken.None).Build(MakeTile())[0];

        Assert.Equal(5, polygon.ExteriorRing.NumPoints);
    }

    [Fact]
    public void Build_WithOnePolygon_CoordinatesAreAbsolute()
    {
        WriteFixture(OnePolygon);

        var polygon = MakeReader(CancellationToken.None).Build(MakeTile())[0];
        var first = polygon.ExteriorRing.Coordinates[0];

        Assert.Equal(384100, first.X, precision: 0);
        Assert.Equal(6672100, first.Y, precision: 0);
    }

    [Fact]
    public void Build_WithEmptyFile_ReturnsEmptyList()
    {
        WriteFixture(string.Empty);

        var result = MakeReader(CancellationToken.None).Build(MakeTile());

        Assert.Empty(result);
    }

    private void WriteFixture(string content) =>
        File.WriteAllText(
            Path.Combine(_tempDir, IWaterAreasBuilder.Filename(TileName, Version)),
            content);

    private WaterAreasReader MakeReader(CancellationToken ct) =>
        new() { CancellationToken = ct, Logger = NullLogger.Instance };

    private Tile MakeTile() =>
        new() { Name = TileName, Common = new TileCommon(256, _tempDir, _tempDir, Version) };
}
