using Kuoste.TerrainEngine.Common.Tiles;
using Xunit;

namespace Kuoste.TerrainEngine.Common.Tests.Tiles;

public class TileCommonTests
{
    [Fact]
    public void EdgeLength_IsOneThousand()
    {
        Assert.Equal(1000, TileCommon.EdgeLength);
    }

    [Fact]
    public void Constructor_StoresAllProperties()
    {
        const int alphamapRes = 256;
        const string intermediate = "/data/intermediate";
        const string original = "/data/original";
        const string version = "2";

        var common = new TileCommon(alphamapRes, intermediate, original, version);

        Assert.Equal(alphamapRes, common.AlphamapResolution);
        Assert.Equal(intermediate, common.DirectoryIntermediate);
        Assert.Equal(original, common.DirectoryOriginal);
        Assert.Equal(version, common.Version);
    }
}
