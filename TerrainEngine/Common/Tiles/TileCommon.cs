using Kuoste.TerrainEngine.Common.Interfaces;
using System.Collections;
using System.Collections.Generic;

namespace Kuoste.TerrainEngine.Common.Tiles
{
    public class TileCommon
    {
        public int AlphamapResolution { get; }

        /// <summary>
        /// Tile edge length in meters
        /// </summary>
        public const int EdgeLength = 1000;
        public string DirectoryIntermediate { get; }
        public string DirectoryOriginal { get; }

        public string Version { get; }

        /// <summary>Tiling scheme that maps tile names to world coordinates. Defaults to NLS Finland.</summary>
        public ITileScheme TileScheme { get; }

        public TileCommon(int alphamapResolution, string directoryIntermediate, string directoryOriginal, string version, ITileScheme? tileScheme = null)
        {
            AlphamapResolution = alphamapResolution;
            DirectoryIntermediate = directoryIntermediate;
            DirectoryOriginal = directoryOriginal;
            Version = version;
            TileScheme = tileScheme ?? new NlsTileScheme();
        }
    }
}
