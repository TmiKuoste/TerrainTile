using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.DemDsm;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.DemDsm;

public class DemDsmReaderTests : IDisposable
{
    // Tile L4133B3_4 decodes to bounds [384000, 385000] x [6672000, 6673000]
    private const string TileName = "L4133B3_4";
    private const string Version = "1";

    private readonly string _tempDir;

    public DemDsmReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TerrainTileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Build_WhenCancelled_ReturnsEmptyVoxelGrid()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var reader = MakeReader(cts.Token);
        var result = reader.Build(MakeTile());

        Assert.Null(result.Dem);
    }

    [Fact]
    public void Build_WithSerializedGrid_ReturnsDeserializedGrid()
    {
        var grid = VoxelGrid.CreateGrid(4, 4, new Envelope(384000, 385000, 6672000, 6673000));
        var filePath = Path.Combine(_tempDir, IDemDsmBuilder.Filename(TileName, Version));
        grid.Serialize(filePath);

        var result = MakeReader(CancellationToken.None).Build(MakeTile());

        Assert.NotNull(result.Dem);
        Assert.Equal(4, result.Dem.GetLength(0));
        Assert.Equal(4, result.Dem.GetLength(1));
    }

    private DemDsmReader MakeReader(CancellationToken ct) =>
        new() { CancellationToken = ct, Logger = NullLogger.Instance };

    private Tile MakeTile() =>
        new() { Name = TileName, Common = new TileCommon(256, _tempDir, _tempDir, Version) };
}
