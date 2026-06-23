using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.Buildings;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.Buildings;

[Collection("NlsIntegration")]
public class BuildingsCreatorTests : IDisposable
{
    private readonly NlsDataFixture _nls;
    private readonly string _tempDir;

    public BuildingsCreatorTests(NlsDataFixture nls)
    {
        _nls = nls;
        _tempDir = Path.Combine(Path.GetTempPath(), "TerrainTileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_WithShapefile_ReturnsBuildings()
    {
        if (_nls.DemDsm == null) return;

        var result = MakeCreator().Build(MakeTile());

        // The Helsinki sample area contains residential and commercial buildings
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_WithShapefile_WritesIntermediateFile()
    {
        if (_nls.DemDsm == null) return;

        MakeCreator().Build(MakeTile());

        var expectedFile = Path.Combine(_tempDir,
            IBuildingsBuilder.Filename(SampleDataHelper.TileName, SampleDataHelper.Version));
        Assert.True(File.Exists(expectedFile), $"Expected intermediate file: {expectedFile}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Build_Buildings_HaveNonZeroVertices()
    {
        if (_nls.DemDsm == null) return;

        var result = MakeCreator().Build(MakeTile());

        Assert.All(result, b => Assert.True(b.Vertices.Length > 0));
    }

    private BuildingsCreator MakeCreator() =>
        new() { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance };

    private Tile MakeTile() => new()
    {
        Name = SampleDataHelper.TileName,
        Common = new TileCommon(256, _tempDir, _nls.NlsDataPath!, SampleDataHelper.Version),
        DemDsm = _nls.DemDsm
    };
}
