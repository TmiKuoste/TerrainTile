using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.Rasters;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using LasUtility.Common;
using LasUtility.Nls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.Rasters;

public class RasterCreatorTests : IDisposable
{
    private readonly string _tempDir;

    public RasterCreatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TerrainTileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_TerrainType_WritesIntermediateFile()
    {
        var dataPath = SampleDataHelper.FindNlsDataPath();
        if (dataPath == null) return;

        var creator = MakeTerrainTypeCreator();
        creator.Build(MakeTile(dataPath));

        var expectedFile = Path.Combine(_tempDir,
            IRasterBuilder.Filename(SampleDataHelper.TileName, IRasterBuilder.SpecifierTerrainType, SampleDataHelper.Version));
        Assert.True(File.Exists(expectedFile), $"Expected intermediate file: {expectedFile}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_TerrainType_ReturnsPopulatedRaster()
    {
        var dataPath = SampleDataHelper.FindNlsDataPath();
        if (dataPath == null) return;

        var result = (ByteRaster)MakeTerrainTypeCreator().Build(MakeTile(dataPath));

        Assert.NotNull(result.Raster);

        // At least some non-zero (classified) pixels expected in the Helsinki area
        int nonZeroCount = 0;
        foreach (var row in result.Raster)
            foreach (byte v in row)
                if (v != ByteRaster.NoDataValue) nonZeroCount++;
        Assert.True(nonZeroCount > 0, "Expected some non-zero terrain type values");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_BuildingsRoads_WritesIntermediateFile()
    {
        var dataPath = SampleDataHelper.FindNlsDataPath();
        if (dataPath == null) return;

        var creator = MakeBuildingsRoadsCreator();
        creator.Build(MakeTile(dataPath));

        var expectedFile = Path.Combine(_tempDir,
            IRasterBuilder.Filename(SampleDataHelper.TileName, IRasterBuilder.SpecifierBuildingsRoads, SampleDataHelper.Version));
        Assert.True(File.Exists(expectedFile), $"Expected intermediate file: {expectedFile}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_BuildingsRoads_ReturnsPopulatedRaster()
    {
        var dataPath = SampleDataHelper.FindNlsDataPath();
        if (dataPath == null) return;

        var result = (ByteRaster)MakeBuildingsRoadsCreator().Build(MakeTile(dataPath));

        Assert.NotNull(result.Raster);

        int nonZeroCount = 0;
        foreach (var row in result.Raster)
            foreach (byte v in row)
                if (v != ByteRaster.NoDataValue) nonZeroCount++;
        Assert.True(nonZeroCount > 0, "Expected some non-zero buildings/roads values");
    }

    private RasterCreator MakeTerrainTypeCreator()
    {
        var c = new RasterCreator { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance };
        c.SetRasterSpecifier(IRasterBuilder.SpecifierTerrainType);

        var classes = new Dictionary<int, byte>();
        foreach (var kv in TopographicDb.WaterPolygonClassesToRasterValues) classes[kv.Key] = kv.Value;
        foreach (var kv in TopographicDb.SwampPolygonClassesToRasterValues) classes[kv.Key] = kv.Value;
        foreach (var kv in TopographicDb.RockPolygonClassesToRasterValues) classes[kv.Key] = kv.Value;
        foreach (var kv in TopographicDb.SandPolygonClassesToRasterValues) classes[kv.Key] = kv.Value;
        foreach (var kv in TopographicDb.FieldPolygonClassesToRasterValues) classes[kv.Key] = kv.Value;
        foreach (var kv in TopographicDb.RockLineClassesToRasterValues) classes[kv.Key] = kv.Value;
        c.SetRasterizedClassesWithRasterValues(classes);

        c.SetShpFilenames(new[]
        {
            TopographicDb.sPrefixForTerrainType + "L4133L" + TopographicDb.sPostfixForPolygon + ".shp"
        });
        return c;
    }

    private RasterCreator MakeBuildingsRoadsCreator()
    {
        var c = new RasterCreator { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance };
        c.SetRasterSpecifier(IRasterBuilder.SpecifierBuildingsRoads);

        var classes = new Dictionary<int, byte>();
        foreach (var kv in TopographicDb.RoadLineClassesToRasterValues) classes[kv.Key] = kv.Value;
        foreach (var kv in TopographicDb.BuildingPolygonClassesToRasterValues) classes[kv.Key] = kv.Value;
        c.SetRasterizedClassesWithRasterValues(classes);

        c.SetShpFilenames(new[]
        {
            TopographicDb.sPrefixForRoads + "L4133L" + TopographicDb.sPostfixForLine + ".shp",
            TopographicDb.sPrefixForBuildings + "L4133L" + TopographicDb.sPostfixForPolygon + ".shp"
        });
        return c;
    }

    private Tile MakeTile(string dataPath) => new()
    {
        Name = SampleDataHelper.TileName,
        Common = new TileCommon(256, _tempDir, dataPath, SampleDataHelper.Version)
    };
}
