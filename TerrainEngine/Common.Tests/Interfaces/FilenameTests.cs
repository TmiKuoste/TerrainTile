using Kuoste.TerrainEngine.Common.Interfaces;
using Xunit;

namespace Kuoste.TerrainEngine.Common.Tests.Interfaces;

public class FilenameTests
{
    private const string TileName = "L4133B3_4";
    private const string Version = "1";

    [Fact]
    public void DemDsmBuilder_Filename_ReturnsExpectedString()
    {
        Assert.Equal("L4133B3_4_DemDsm_v1.voxelgrid", IDemDsmBuilder.Filename(TileName, Version));
    }

    [Fact]
    public void BuildingsBuilder_Filename_ReturnsExpectedString()
    {
        Assert.Equal("L4133B3_4_buildings_v1.geojson", IBuildingsBuilder.Filename(TileName, Version));
    }

    [Fact]
    public void TreeBuilder_Filename_ReturnsExpectedString()
    {
        Assert.Equal("L4133B3_4_trees_v1.geojson", ITreeBuilder.Filename(TileName, Version));
    }

    [Fact]
    public void WaterAreasBuilder_Filename_ReturnsExpectedString()
    {
        Assert.Equal("L4133B3_4_waterareas_v1.geojson", IWaterAreasBuilder.Filename(TileName, Version));
    }

    [Fact]
    public void RasterBuilder_Filename_WithTerrainTypeSpecifier_ReturnsExpectedString()
    {
        Assert.Equal(
            "L4133B3_4_terraintype_v1.asp",
            IRasterBuilder.Filename(TileName, IRasterBuilder.SpecifierTerrainType, Version));
    }

    [Fact]
    public void RasterBuilder_Filename_WithBuildingsRoadsSpecifier_ReturnsExpectedString()
    {
        Assert.Equal(
            "L4133B3_4_buildingsroads_v1.asp",
            IRasterBuilder.Filename(TileName, IRasterBuilder.SpecifierBuildingsRoads, Version));
    }
}
