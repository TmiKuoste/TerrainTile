using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.Buildings;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.Buildings;

public class BuildingsReaderTests : IDisposable
{
    // Tile L4133B3_4 decodes to bounds [384000, 385000] x [6672000, 6673000]
    private const string TileName = "L4133B3_4";
    private const string Version = "1";

    // One building: 1 roof triangle + 1 wall segment.
    // All coordinates are in absolute ETRS-TM35FIN metres.
    private const string OneBuilding =
        "GeometryCollection " +
        "Polygon [384001,6672001,10] [384002,6672001,10] [384001,6672002,10] [384001,6672001,10] " +
        "Polygon [384001,6672001,10] [384002,6672001,10]";

    private readonly string _tempDir;

    public BuildingsReaderTests()
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
    public void Build_WithOneBuilding_ReturnsSingleBuilding()
    {
        WriteFixture(OneBuilding);

        var result = MakeReader(CancellationToken.None).Build(MakeTile());

        Assert.Single(result);
    }

    [Fact]
    public void Build_WithOneBuilding_RoofVerticesAreRelativeToTileOrigin()
    {
        WriteFixture(OneBuilding);

        var b = MakeReader(CancellationToken.None).Build(MakeTile())[0];

        // Roof: (384001 - 384000, 6672001 - 6672000) = (1, 1)
        Assert.Equal(1.0f, b.Vertices[0].X);
        Assert.Equal(1.0f, b.Vertices[0].Y);
        Assert.Equal(10.0f, b.Vertices[0].Z);
    }

    [Fact]
    public void Build_WithOneBuilding_SubmeshSeparatorIsAfterRoofTriangles()
    {
        WriteFixture(OneBuilding);

        var b = MakeReader(CancellationToken.None).Build(MakeTile())[0];

        // 1 roof triangle → 3 indices before walls begin
        Assert.Equal(3, b.iSubmeshSeparator);
    }

    private void WriteFixture(string content) =>
        File.WriteAllText(
            Path.Combine(_tempDir, IBuildingsBuilder.Filename(TileName, Version)),
            content);

    private BuildingsReader MakeReader(CancellationToken ct) =>
        new() { CancellationToken = ct, Logger = NullLogger.Instance };

    private Tile MakeTile() =>
        new() { Name = TileName, Common = new TileCommon(256, _tempDir, _tempDir, Version) };
}
