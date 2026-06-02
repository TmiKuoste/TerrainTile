using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using LasUtility.Common;
using LasUtility.Nls;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using System;
using System.Collections.Generic;
using System.IO;

namespace Kuoste.TerrainEngine.TileBuilders.WaterAreas
{
    public class WaterAreasCreator : Builder, IWaterAreasBuilder
    {
        public List<Polygon> Build(Tile tile)
        {
            List<Polygon> waterAreas = new();

            if (IsCancellationRequested())
                return waterAreas;

            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope envBounds);
            GeometryFactory factory = new();
            Geometry bounds = factory.ToGeometry(envBounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)envBounds.MinX, (int)envBounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);

            string sFullFilename = Path.Combine(tile.Common.DirectoryOriginal, TopographicDb.sPrefixForTerrainType + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");

            Feature[] features = Shapefile.ReadAllFeatures(sFullFilename);

            string sOutputFilename = Path.Combine(tile.Common.DirectoryIntermediate, IWaterAreasBuilder.Filename(tile.Name, tile.Common.Version));
            string sOutputTempName = sOutputFilename + ".tmp";
            using StreamWriter streamWriter = new(sOutputTempName);

            foreach (Feature f in features)
            {
                if (IsCancellationRequested())
                {
                    streamWriter.Close();
                    File.Delete(sOutputTempName);
                    return waterAreas;
                }

                int classification = (int)(long)f.Attributes["LUOKKA"];

                if (true == TopographicDb.WaterPolygonClassesToRasterValues.ContainsKey(classification))
                {
                    Geometry intersection = f.Geometry.Intersection(bounds);

                    if (intersection != Polygon.Empty)
                    {
                        for (int g = 0; g < intersection.NumGeometries; g++)
                        {
                            Polygon p = (Polygon)intersection.GetGeometryN(g);

                            double dHeight = double.MaxValue;

                            foreach (Coordinate c in p.ExteriorRing.Coordinates)
                            {
                                // Geometry.Intersection returns points on the bounds upper borders, but they are not part of the tile area.
                                if (c.X >= tile.DemDsm.Bounds.MaxX)
                                    c.X -= RasterBounds.dEpsilon;

                                if (c.Y >= tile.DemDsm.Bounds.MaxY)
                                    c.Y -= RasterBounds.dEpsilon;


                                // Just pick the lowest point in lake boundary for the surface height
                                double h = tile.DemDsm.GetValue(c);

                                if (!double.IsNaN(h))
                                    dHeight = Math.Min(dHeight, h);
                            }

                            if (dHeight == double.MaxValue)
                            {
                                Logger.LogError($"Tile {tile.Name}: Cannot find surface height for a lake ({p.ExteriorRing.Coordinates.Length} polygon corners).");
                                continue;
                            }

                            List<CoordinateZ> coords = new();
                            foreach (Coordinate c in p.ExteriorRing.Coordinates)
                            {
                                coords.Add(new CoordinateZ(Math.Round(c.X, 2), Math.Round(c.Y, 2), Math.Round(dHeight, 2)));
                            }

                            streamWriter.Write("{ \"type\":\"Polygon\", \"coordinates\": ");
                            streamWriter.Write("[[");

                            for (int i = 0; i < coords.Count; i++)
                            {
                                streamWriter.Write($"[{coords[i].X},{coords[i].Y},{coords[i].Z}]");

                                if (i < coords.Count - 1)
                                {
                                    streamWriter.Write(",");
                                }
                            }

                            // End polygon
                            streamWriter.Write("]]");
                            streamWriter.WriteLine("}");

                            waterAreas.Add(new Polygon(new LinearRing(coords.ToArray())));
                        }
                    }
                }
            }

            streamWriter.Close();
            File.Move(sOutputTempName, sOutputFilename);

            return waterAreas;
        }
    }
}
