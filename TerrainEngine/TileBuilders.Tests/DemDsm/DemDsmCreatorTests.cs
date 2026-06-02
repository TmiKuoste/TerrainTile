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

    private Tile MakeTile(string dataPath) => new()
    {
        Name = SampleDataHelper.TileName,
        Common = new TileCommon(256, _tempDir, dataPath, SampleDataHelper.Version)
    };
}
