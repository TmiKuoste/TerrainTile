using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using Kuoste.TerrainEngine.TileBuilders.Trees;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.Trees;

public class TreeReaderTests : IDisposable
{
    // Tile L4133B3_4 decodes to bounds [384000, 385000] x [6672000, 6673000]
    private const string TileName = "L4133B3_4";
    private const string Version = "1";

    // Two trees at absolute coordinates; EdgeLength = 1000
    private const string TwoTrees =
        "Point [384500,6672500,15] Point [384600,6672600,20]";

    private readonly string _tempDir;

    public TreeReaderTests()
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
    public void Build_WithTwoTrees_ReturnsTwoPoints()
    {
        WriteFixture(TwoTrees);

        var result = MakeReader(CancellationToken.None).Build(MakeTile());

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Build_NormalizesCoordinatesRelativeToTile()
    {
        WriteFixture(TwoTrees);

        var trees = MakeReader(CancellationToken.None).Build(MakeTile());

        // (384500 - 384000) / 1000 = 0.5
        Assert.Equal(0.5, trees[0].X, precision: 5);
        Assert.Equal(0.5, trees[0].Y, precision: 5);
        Assert.Equal(15.0, trees[0].Z, precision: 5);
    }

    [Fact]
    public void Build_WithEmptyFile_ReturnsEmptyList()
    {
        WriteFixture(string.Empty);

        var result = MakeReader(CancellationToken.None).Build(MakeTile());

        Assert.Empty(result);
    }

    private void WriteFixture(string content) =>
        File.WriteAllText(
            Path.Combine(_tempDir, ITreeBuilder.Filename(TileName, Version)),
            content);

    private TreeReader MakeReader(CancellationToken ct) =>
        new() { CancellationToken = ct, Logger = NullLogger.Instance };

    private Tile MakeTile() =>
        new() { Name = TileName, Common = new TileCommon(256, _tempDir, _tempDir, Version) };
}
