using System;
using System.IO;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;

internal static class SampleDataHelper
{
    // Tile L4133B1_5 is the centre 1km sub-tile of the L4133B1 3km point cloud,
    // and lies within the L4133L 12km topographic-db coverage area.
    internal const string TileName = "L4133B1_5";
    internal const string Version = "1";

    // L4133B1_5 = east-centre, north-centre sub-tile → [381000,382000] × [6673000,6674000]
    internal const int TileMinEast = 381000;
    internal const int TileMinNorth = 6673000;

    private static readonly string NlsRelativePath = Path.Combine(
        "fi.kuoste.terraintile", "Samples", "Helsinki9km2", "DataNlsFinland");

    /// <summary>
    /// Walks up from the test assembly directory until it finds the NLS sample-data folder.
    /// Returns null when run without the full repo (CI without LFS data, etc.).
    /// </summary>
    internal static string? FindNlsDataPath()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, NlsRelativePath);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
