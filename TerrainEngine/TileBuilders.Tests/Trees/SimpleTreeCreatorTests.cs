using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using Kuoste.TerrainEngine.TileBuilders.Trees;
using LasUtility.Common;
using LasUtility.Nls;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.Trees;

public class SimpleTreeCreatorTests : IDisposable
{
    // L4133B1_5 = [381000,382000] × [6673000,6674000]
    private const string TileName = "L4133B1_5";
    private const int MinEast = SampleDataHelper.TileMinEast;
    private const int MinNorth = SampleDataHelper.TileMinNorth;

    // The grid is made 10 % larger than the tile on all four sides to mirror production,
    // where the DemDsm always has overlap. This keeps tile bounds strictly interior to
    // the grid so ProjToCell never hits the exclusive-max boundary.
    private const int OverlapMeters = 50;
    private const int OutputEdgeMeters = 1000; // default TileCommon.OutputEdgeLength
    private const int GridEdgeMeters = OutputEdgeMeters + 2 * OverlapMeters; // 1100 m
    private const int GridSize = 110;  // 10 m/cell
    // Tree is placed at the grid cell that maps to the tile centre
    private const int CenterRow = GridSize / 2; // 55
    private const int CenterCol = GridSize / 2; // 55

    private const float GroundHeight = 50.0f;
    private const float VegHeight = 65.0f;   // 15 m above ground → IsHighVegetation ✓

    private readonly string _tempDir;

    public SimpleTreeCreatorTests()
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

        var (tile, _) = CreateSyntheticTile();
        var result = MakeCreator(cts.Token).Build(tile);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_WithNoVegetationPoints_ReturnsNoTrees()
    {
        var (tile, _) = CreateSyntheticTile();

        var result = MakeCreator(CancellationToken.None).Build(tile);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_WithInsufficientNeighbourhoodVegetation_ReturnsNoTrees()
    {
        var (tile, grid) = CreateSyntheticTile();

        // Add only 3 high-vegetation points in the same cell — fewer than the required 5
        grid.GetGridCoordinates(CenterRow, CenterCol, out double cx, out double cy);
        for (int i = 0; i < 3; i++)
            grid.AddPoint(cx + 1, cy + 1, VegHeight, (byte)PointCloud05p.Classes.HighVegetation, false);

        grid.SortAndTrim();

        var result = MakeCreator(CancellationToken.None).Build(tile);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_WithEnoughHighVegetation_ReturnsTree()
    {
        var (tile, grid) = CreateSyntheticTile();
        AddTreeAtCell(grid, CenterRow, CenterCol);

        var result = MakeCreator(CancellationToken.None).Build(tile);

        Assert.NotEmpty(result);
    }

    [Fact]
    public void Build_TreePosition_IsNormalisedToTileFraction()
    {
        var (tile, grid) = CreateSyntheticTile();
        AddTreeAtCell(grid, CenterRow, CenterCol);

        var result = MakeCreator(CancellationToken.None).Build(tile);

        Assert.Single(result);
        // Coordinates are (x - MinEast) / EdgeLength  →  must be in [0, 1]
        Assert.InRange(result[0].X, 0.0, 1.0);
        Assert.InRange(result[0].Y, 0.0, 1.0);
    }

    [Fact]
    public void Build_WritesIntermediateGeojsonFile()
    {
        var (tile, grid) = CreateSyntheticTile();
        AddTreeAtCell(grid, CenterRow, CenterCol);

        MakeCreator(CancellationToken.None).Build(tile);

        var expectedFile = Path.Combine(_tempDir, ITreeBuilder.Filename(TileName, SampleDataHelper.Version));
        Assert.True(File.Exists(expectedFile));
    }

    // ---- helpers ----

    private (Tile tile, VoxelGrid grid) CreateSyntheticTile()
    {
        // Extend beyond the tile on all sides so tile bounds are strictly interior
        var extent = new Envelope(
            MinEast - OverlapMeters, MinEast + OutputEdgeMeters + OverlapMeters,
            MinNorth - OverlapMeters, MinNorth + OutputEdgeMeters + OverlapMeters);

        var grid = VoxelGrid.CreateGrid(GridSize, GridSize, extent);

        // Fill in ground heights so GetValue(row,col) never returns NaN
        for (int r = 0; r < GridSize; r++)
            for (int c = 0; c < GridSize; c++)
                grid.Dem[r, c] = GroundHeight;

        var raster = new ByteRaster();
        raster.InitializeRaster(GridSize, GridSize, extent);

        var tile = new Tile
        {
            Name = TileName,
            Common = new TileCommon(256, _tempDir, _tempDir, SampleDataHelper.Version),
            DemDsm = grid,
            BuildingsRoads = raster
        };

        return (tile, grid);
    }

    /// <summary>
    /// Places a dominant tree at (centerRow, centerCol) with enough neighbourhood
    /// vegetation to satisfy SimpleTreeCreator's threshold of 5 high-vegetation points.
    /// </summary>
    private static void AddTreeAtCell(VoxelGrid grid, int centerRow, int centerCol)
    {
        const byte highVeg = (byte)PointCloud05p.Classes.HighVegetation;

        // Center cell: highest point in neighbourhood
        grid.GetGridCoordinates(centerRow, centerCol, out double cx, out double cy);
        grid.AddPoint(cx + 1, cy + 1, VegHeight, highVeg, false);

        // 8 adjacent cells at slightly lower height → total 9 points ≥ 5 required
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                grid.GetGridCoordinates(centerRow + dr, centerCol + dc, out double nx, out double ny);
                grid.AddPoint(nx + 1, ny + 1, VegHeight - 2.0f, highVeg, false);
            }
        }

        grid.SortAndTrim();
    }

    private SimpleTreeCreator MakeCreator(CancellationToken ct) =>
        new() { CancellationToken = ct, Logger = NullLogger.Instance };
}
