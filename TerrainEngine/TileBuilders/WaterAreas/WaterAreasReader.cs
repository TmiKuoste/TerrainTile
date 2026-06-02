using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Kuoste.TerrainEngine.TileBuilders.WaterAreas
{
    public class WaterAreasReader : Builder, IWaterAreasBuilder
    {
        public List<Polygon> Build(Tile tile)
        {
            List<Polygon> polygons = new();

            if (IsCancellationRequested())
                return polygons;

            string sFullFilename = Path.Combine(tile.Common.DirectoryIntermediate, IWaterAreasBuilder.Filename(tile.Name, tile.Common.Version));

            string[] sAreas = File.ReadAllText(sFullFilename).Split("Polygon");

            foreach (string sArea in sAreas)
            {
                if (IsCancellationRequested())
                    return polygons;

                string[] sCordinates = sArea.Split("[", StringSplitOptions.RemoveEmptyEntries);

                List<CoordinateZ> coordinates = new();

                foreach (string sCoordinate in sCordinates)
                {
                    if (!char.IsDigit(sCoordinate[0]))
                        continue;

                    var coords = sCoordinate.Split(",", StringSplitOptions.RemoveEmptyEntries);

                    // Delete the last character which is a closing bracket and everyting after it
                    coords[2] = coords[2][..coords[2].IndexOf(']')];

                    coordinates.Add(new(
                        double.Parse(coords[0], CultureInfo.InvariantCulture),
                        double.Parse(coords[1], CultureInfo.InvariantCulture),
                        double.Parse(coords[2], CultureInfo.InvariantCulture)));
                }

                if (coordinates.Count > 0)
                {
                    polygons.Add(new Polygon(new LinearRing(coordinates.ToArray())));
                }
            }

            return polygons;
        }
    }
}
