using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.DemDsm;
using LasUtility.VoxelGrid;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;

/// <summary>
/// xUnit collection fixture that runs DemDsmCreator once against the NLS sample data
/// and shares the resulting VoxelGrid across all tests in the NlsIntegration collection.
/// </summary>
public sealed class NlsDataFixture : IDisposable
{
    public string? NlsDataPath { get; }

    /// <summary>Shared temp directory used for the DemDsm intermediate file.</summary>
    public string TempDir { get; }

    /// <summary>
    /// DemDsm for tile <see cref="SampleDataHelper.TileName"/>, or null when NLS data
    /// is not available (e.g. on a CI agent without the full repo).
    /// </summary>
    public VoxelGrid? DemDsm { get; }

    public NlsDataFixture()
    {
        NlsDataPath = SampleDataHelper.FindNlsDataPath();

        TempDir = Path.Combine(Path.GetTempPath(), "TerrainTileTests_NlsFixture_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);

        if (NlsDataPath == null)
            return;

        var tile = new Tile
        {
            Name = SampleDataHelper.TileName,
            Common = new TileCommon(256, TempDir, NlsDataPath, SampleDataHelper.Version)
        };

        var creator = new DemDsmCreator
        {
            CancellationToken = CancellationToken.None,
            Logger = NullLogger.Instance
        };

        DemDsm = creator.Build(tile);
    }

    public void Dispose()
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
    }
}

[CollectionDefinition("NlsIntegration")]
public class NlsIntegrationCollection : ICollectionFixture<NlsDataFixture> { }
