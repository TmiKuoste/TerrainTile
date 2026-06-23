using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.Rasters;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using LasUtility.Common;
using NetTopologySuite.Geometries;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.Rasters;

public class RasterReaderTests : IDisposable
{
    private const string TileName = "L4133B3_4";
    private const string Version = "1";
    private const string Specifier = "terraintype";

    private readonly string _tempDir;

    public RasterReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TerrainTileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Build_WhenCancelled_ReturnsEmptyRaster()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = MakeReader(cts.Token).Build(MakeTile());

        Assert.IsType<ByteRaster>(result);
        Assert.Null(((ByteRaster)result).Raster);
    }

    [Fact]
    public void Build_WithSerializedRaster_ReturnsCorrectDimensions()
    {
        var raster = new ByteRaster();
        raster.InitializeRaster(3, 4, new Envelope(384000, 385000, 6672000, 6673000));
        raster.Raster[0][0] = 42;
        raster.WriteAsAscii(Path.Combine(_tempDir, IRasterBuilder.Filename(TileName, Specifier, Version)));

        var result = (ByteRaster)MakeReader(CancellationToken.None).Build(MakeTile());

        Assert.Equal(3, result.Raster.Length);
        Assert.Equal(4, result.Raster[0].Length);
    }

    [Fact]
    public void Build_WithSerializedRaster_PreservesValues()
    {
        var raster = new ByteRaster();
        raster.InitializeRaster(3, 4, new Envelope(384000, 385000, 6672000, 6673000));
        raster.Raster[0][0] = 42;
        raster.WriteAsAscii(Path.Combine(_tempDir, IRasterBuilder.Filename(TileName, Specifier, Version)));

        var result = (ByteRaster)MakeReader(CancellationToken.None).Build(MakeTile());

        Assert.Equal(42, result.Raster[0][0]);
    }

    private RasterReader MakeReader(CancellationToken ct)
    {
        var r = new RasterReader { CancellationToken = ct, Logger = NullLogger.Instance };
        r.SetRasterSpecifier(Specifier);
        return r;
    }

    private Tile MakeTile() =>
        new() { Name = TileName, Common = new TileCommon(256, _tempDir, _tempDir, Version) };
}
