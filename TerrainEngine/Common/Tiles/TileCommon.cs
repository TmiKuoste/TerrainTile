using Kuoste.TerrainEngine.Common.Interfaces;
using System.Collections;
using System.Collections.Generic;

namespace Kuoste.TerrainEngine.Common.Tiles
{
    public class TileCommon
    {
        public int AlphamapResolution { get; }

        public string DirectoryIntermediate { get; }
        public string DirectoryOriginal { get; }

        public string Version { get; }

        /// <summary>Edge length (m) of a consumption / output tile — one cached voxelgrid, one Unity terrain.</summary>
        public int OutputEdgeLength { get; }

        /// <summary>Edge length (m) of a triangulation block. Carried now, consumed in Phase 3; today equals the output tile.</summary>
        public int BlockEdgeLength { get; }

        /// <summary>Edge length (m) of a source point-cloud tile (the .laz extent).</summary>
        public int SourceEdgeLength { get; }

        /// <summary>Tiling scheme that maps tile names to world coordinates. Defaults to NLS Finland.</summary>
        public ITileScheme TileScheme { get; }

        public TileCommon(int alphamapResolution, string directoryIntermediate, string directoryOriginal, string version,
            ITileScheme? tileScheme = null, int outputEdgeLength = 1000, int blockEdgeLength = 1000, int sourceEdgeLength = 3000)
        {
            AlphamapResolution = alphamapResolution;
            DirectoryIntermediate = directoryIntermediate;
            DirectoryOriginal = directoryOriginal;
            Version = version;
            TileScheme = tileScheme ?? new NlsTileScheme();
            OutputEdgeLength = outputEdgeLength;
            BlockEdgeLength = blockEdgeLength;
            SourceEdgeLength = sourceEdgeLength;
        }
    }
}
