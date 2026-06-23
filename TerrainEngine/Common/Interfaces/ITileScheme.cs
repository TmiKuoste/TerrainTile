using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace Kuoste.TerrainEngine.Common.Interfaces
{
    /// <summary>
    /// Maps between tile names and world coordinates for one tiling scheme
    /// (NLS Finland map sheets today; a generic grid for other countries / output).
    /// Keeps the engine independent of any single country's naming.
    /// </summary>
    public interface ITileScheme
    {
        /// <summary>Coordinate reference of this scheme's world coordinates (NLS = 3067).</summary>
        int Epsg { get; }

        /// <summary>World-coordinate bounds of a named tile.</summary>
        Envelope Decode(string tileName);

        /// <summary>Name of the tile of the given edge length that contains (x, y).</summary>
        string Encode(double x, double y, int edgeLengthMeters);

        /// <summary>Edge length in metres encoded by a tile name.</summary>
        int EdgeLengthMeters(string tileName);

        /// <summary>The (larger) tile of the given edge length that contains this tile.</summary>
        string ParentTile(string tileName, int edgeLengthMeters);

        /// <summary>Sub-tiles of the given (smaller) edge length within a tile.</summary>
        IEnumerable<string> SubTiles(string tileName, int edgeLengthMeters);

        /// <summary>All tiles of the given edge length intersecting the bounds.</summary>
        IEnumerable<string> TilesInBounds(Envelope bounds, int edgeLengthMeters);

        /// <summary>Same-size edge/corner neighbours (for halo stitching).</summary>
        IEnumerable<string> Neighbors(string tileName);
    }
}
