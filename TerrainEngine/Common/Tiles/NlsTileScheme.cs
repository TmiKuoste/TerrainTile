using Kuoste.TerrainEngine.Common.Interfaces;
using LasUtility.Nls;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;

namespace Kuoste.TerrainEngine.Common.Tiles
{
    /// <summary>
    /// <see cref="ITileScheme"/> for the National Land Survey of Finland map-sheet grid
    /// (ETRS-TM35FIN / EPSG:3067), wrapping <see cref="TileNamer"/>.
    /// </summary>
    public class NlsTileScheme : ITileScheme
    {
        public int Epsg => 3067;

        public Envelope Decode(string tileName)
        {
            TileNamer.Decode(tileName, out Envelope bounds);
            return bounds;
        }

        public string Encode(double x, double y, int edgeLengthMeters)
            => TileNamer.Encode((int)x, (int)y, edgeLengthMeters);

        public int EdgeLengthMeters(string tileName)
            => (int)Math.Round(Decode(tileName).Width);

        public string ParentTile(string tileName, int edgeLengthMeters)
        {
            Envelope e = Decode(tileName);
            return Encode(e.MinX, e.MinY, edgeLengthMeters);
        }

        public IEnumerable<string> SubTiles(string tileName, int edgeLengthMeters)
            => TilesInBounds(Decode(tileName), edgeLengthMeters);

        public IEnumerable<string> TilesInBounds(Envelope bounds, int edgeLengthMeters)
        {
            // Align the start to the grid via the tile containing the lower-left corner.
            Envelope start = Decode(Encode(bounds.MinX, bounds.MinY, edgeLengthMeters));

            for (double x = start.MinX; x < bounds.MaxX; x += edgeLengthMeters)
            {
                for (double y = start.MinY; y < bounds.MaxY; y += edgeLengthMeters)
                {
                    yield return Encode(x, y, edgeLengthMeters);
                }
            }
        }

        public IEnumerable<string> Neighbors(string tileName)
        {
            Envelope e = Decode(tileName);
            int edge = (int)Math.Round(e.Width);

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    // Use the neighbour tile centre to avoid boundary ambiguity.
                    double cx = e.MinX + (dx + 0.5) * edge;
                    double cy = e.MinY + (dy + 0.5) * edge;
                    yield return Encode(cx, cy, edge);
                }
            }
        }
    }
}
