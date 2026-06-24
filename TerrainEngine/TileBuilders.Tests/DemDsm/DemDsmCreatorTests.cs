using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.DemDsm;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.DemDsm;

public class DemDsmCreatorTests : IDisposable
{
    private readonly string _tempDir;

    public DemDsmCreatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TerrainTileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_WithLazFile_ReturnsDemWithValues()
    {
        var dataPath = SampleDataHelper.FindNlsDataPath();
        if (dataPath == null) return;

        var creator = new DemDsmCreator { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance };
        var result = creator.Build(MakeTile(dataPath));

        Assert.NotNull(result.Dem);

        // At least some cells should have valid elevation data
        int nonNanCount = 0;
        foreach (float v in result.Dem)
            if (!float.IsNaN(v)) nonNanCount++;
        Assert.True(nonNanCount > 0, "Expected non-NaN DEM values in Helsinki tile");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_WithLazFile_WritesIntermediateFile()
    {
        var dataPath = SampleDataHelper.FindNlsDataPath();
        if (dataPath == null) return;

        var creator = new DemDsmCreator { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance };
        creator.Build(MakeTile(dataPath));

        var expectedFile = Path.Combine(_tempDir, IDemDsmBuilder.Filename(SampleDataHelper.TileName, SampleDataHelper.Version));
        Assert.True(File.Exists(expectedFile), $"Expected intermediate file: {expectedFile}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_WhenIntermediateFileAlreadyExists_ReadsFromCache()
    {
        var dataPath = SampleDataHelper.FindNlsDataPath();
        if (dataPath == null) return;

        // First pass: create from LAZ
        var creator1 = new DemDsmCreator { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance };
        creator1.Build(MakeTile(dataPath));

        // Second pass: a fresh Creator instance should read from cache
        var creator2 = new DemDsmCreator { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance };
        var result = creator2.Build(MakeTile(dataPath));

        Assert.NotNull(result.Dem);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_WholeBlock_ProducesEquivalentDemToSubmesh()
    {
        var dataPath = SampleDataHelper.FindNlsDataPath();
        if (dataPath == null) return;

        // Default path: the 3 km source is triangulated as 9 separate 1 km submesh blocks.
        var submesh = new DemDsmCreator { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance }
            .Build(MakeTile(dataPath, blockEdge: 1000));

        // Opt-in path: the whole 3 km source is triangulated as a single block (no internal seams).
        var wholeBlock = new DemDsmCreator { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance }
            .Build(MakeTile(dataPath, blockEdge: 3000));

        Assert.NotNull(wholeBlock.Dem);
        Assert.Equal(submesh.Dem.GetLength(0), wholeBlock.Dem.GetLength(0));
        Assert.Equal(submesh.Dem.GetLength(1), wholeBlock.Dem.GetLength(1));

        // The centre tile is interior to the source, so both meshes resolve the same local triangles.
        // Differences are confined to the grid's outer overlap ring, where the submesh mesh ends but
        // the whole-block mesh continues — so the vast majority of cells must agree.
        int compared = 0, agree = 0;
        for (int r = 0; r < submesh.Dem.GetLength(0); r++)
        {
            for (int c = 0; c < submesh.Dem.GetLength(1); c++)
            {
                float a = submesh.Dem[r, c], b = wholeBlock.Dem[r, c];
                if (float.IsNaN(a) || float.IsNaN(b)) continue;
                compared++;
                if (Math.Abs(a - b) <= 0.5f) agree++;
            }
        }

        Assert.True(compared > 0, "Expected overlapping non-NaN cells to compare");
        Assert.True(agree / (double)compared > 0.95,
            $"Whole-block DEM should match submesh DEM for the interior tile; agreed {agree}/{compared}");
    }

    private Tile MakeTile(string dataPath, int blockEdge = 1000) => new()
    {
        Name = SampleDataHelper.TileName,
        Common = new TileCommon(256, _tempDir, dataPath, SampleDataHelper.Version,
            tileScheme: null, outputEdgeLength: 1000, blockEdgeLength: blockEdge, sourceEdgeLength: 3000)
    };
}
