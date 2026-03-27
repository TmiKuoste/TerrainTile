using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using LasUtility.Common;
using LasUtility.DEM;
using LasUtility.LAS;
using LasUtility.Nls;
using LasUtility.VoxelGrid;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using System;
using System.Collections.Generic;
using System.IO;

namespace Kuoste.TerrainEngine.TileBuilders.Buildings
{
    public class BuildingsCreator : Builder, IBuildingsBuilder
    {
        private const int _iRequiredBuildingHeights = 7;
        private const double _dPercentileForBuildingHeight = 0.8;
        private const int _iRoofPointsSkip = 2;
        private const float _fDefaultBuildingHeight = 3.5f;

        public List<Tile.Building> Build(Tile tile)
        {
            if (IsCancellationRequested())
                return new();

            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);

            string sOutputFilename = Path.Combine(tile.Common.DirectoryIntermediate, IBuildingsBuilder.Filename(tile.Name, tile.Common.Version));
            string sOutputTempName = sOutputFilename + ".tmp";
            using StreamWriter streamWriter = new(sOutputTempName);

            string sFullFilename = Path.Combine(tile.Common.DirectoryOriginal, TopographicDb.sPrefixForBuildings + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");
            Feature[] features = Shapefile.ReadAllFeatures(sFullFilename);

            GeometryFactory factory = new();
            Geometry geometryTileBounds = factory.ToGeometry(bounds);

            int iDefaultHeightCount = 0;

            foreach (Feature f in features)
            {
                if (IsCancellationRequested())
                {
                    streamWriter.Close();
                    File.Delete(sOutputTempName);
                    return new();
                }

                int classification = (int)(long)f.Attributes["LUOKKA"];

                if (false == TopographicDb.BuildingPolygonClassesToRasterValues.ContainsKey(classification))
                {
                    continue;
                }

                Geometry intersection = f.Geometry.Intersection(geometryTileBounds);

                if (intersection == Polygon.Empty)
                {
                    // The whole building is outside the tile
                    continue;
                }

                // Go through polygons in the intersection
                for (int j = 0; j < intersection.NumGeometries; j++)
                {
                    Polygon partialBuilding = (Polygon)intersection.GetGeometryN(j);

                    //LineString buildingExterior = partialBuilding.ExteriorRing;

                    //Polygon buildingPolygonExteriorOnly = new(new LinearRing(buildingExterior.Coordinates));

                    List<float> buildingHeights = new();
                    List<float> buildingGroundHeights = new();

                    for (int i = 0; i < partialBuilding.ExteriorRing.NumPoints; i++)
                    {
                        Coordinate c = partialBuilding.ExteriorRing.GetCoordinateN(i);

                        // Geometry.Intersection returns points that are on the higher bounds. Move them a little so that they are inside our area.
                        if (c.X == bounds.MaxX)
                            c.X -= RasterBounds.dEpsilon;
                        if (c.Y == bounds.MaxY)
                            c.Y -= RasterBounds.dEpsilon;

                        // Get building height at the coordinate
                        tile.DemDsm.GetGridIndexes(c.X, c.Y, out int iX, out int iY);
                        BinPoint bp = tile.DemDsm.GetHighestPointInClassRange(iX, iY, 0, byte.MaxValue);

                        if (bp != null)
                        {
                            buildingHeights.Add(bp.Z);
                        }

                        double dGroundHeight = tile.DemDsm.GetValue(c);
                        if (!double.IsNaN(dGroundHeight))
                        {
                            buildingGroundHeights.Add((float)dGroundHeight);
                        }
                    }

                    if (buildingGroundHeights.Count == 0)
                    {
                        Logger.LogError("No ground height for a building found.");
                        continue;
                    }

                    buildingGroundHeights.Sort();
                    float fLowestGroundHeight = buildingGroundHeights[0];

                    // Create roof triangulation

                    // Extend bounds to next integer coordinates

                    Envelope buildingBounds = partialBuilding.EnvelopeInternal;
                    Envelope buildingBoundsRounded = new(
                        Math.Floor(buildingBounds.MinX), Math.Ceiling(buildingBounds.MaxX),
                        Math.Floor(buildingBounds.MinY), Math.Ceiling(buildingBounds.MaxY));

                    // For triangulation, move coordinates to origo
                    int iLineCount = (int)(buildingBoundsRounded.MaxY - buildingBoundsRounded.MinY);
                    int iColumnCount = (int)(buildingBoundsRounded.MaxX - buildingBoundsRounded.MinX);
                    SurfaceTriangulation tri = new(iLineCount, iColumnCount,
                        0, 0, buildingBoundsRounded.MaxX - buildingBoundsRounded.MinX,
                        buildingBoundsRounded.MaxY - buildingBoundsRounded.MinY, false);

                    // Make faster by skipping some coordinates
                    for (int x = (int)buildingBoundsRounded.MinX; x < buildingBoundsRounded.MaxX; x += _iRoofPointsSkip)
                    {
                        for (int y = (int)buildingBoundsRounded.MinY; y < buildingBoundsRounded.MaxY; y += _iRoofPointsSkip)
                        {
                            tile.DemDsm.GetGridIndexes(x, y, out int iRow, out int jCol);
                            BinPoint bp = tile.DemDsm.GetHighestPointInClassRange(iRow, jCol, 0, byte.MaxValue);

                            if (bp != null && partialBuilding.Contains(new Point(x, y)))
                            {
                                tri.AddPoint(new LasPoint() { x = x - buildingBounds.MinX, y = y - buildingBounds.MinY });

                                buildingHeights.Add(bp.Z);
                            }
                        }
                    }


                    // Add also points along the building polygon to get a proper triangulation
                    AddPointsAlongPolygon(partialBuilding.ExteriorRing, buildingBounds, tri);

                    foreach (LineString ls in partialBuilding.InteriorRings)
                    {
                        AddPointsAlongPolygon(ls, buildingBounds, tri);
                    }

                    if (tri.PointCount < 4)
                    {
                        Logger.LogError("Not enough points for triangulation");
                        continue;
                    }

                    try
                    {
                        tri.Create();
                    }
                    catch (Exception e)
                    {
                        Logger.LogException(e);
                        continue;
                    }

                    float fBuildingHeight;

                    if (buildingHeights.Count >= _iRequiredBuildingHeights)
                    {
                        // Take a percentile of building heights. Aiming for the actual roof height, not the walls, inner yards or overhanging trees.
                        buildingHeights.Sort();
                        fBuildingHeight = buildingHeights[(int)(buildingHeights.Count * _dPercentileForBuildingHeight)];
                    }
                    else
                    {
                        iDefaultHeightCount++;
                        fBuildingHeight = fLowestGroundHeight + _fDefaultBuildingHeight;
                    }

                    // Start geometrycollection
                    streamWriter.WriteLine("{ \"type\":\"GeometryCollection\", \"geometries\": [");

                    // Add roof vertices
                    int iTriangleCount = tri.GetTriangleCount();
                    for (int i = 0; i < iTriangleCount; i++)
                    {
                        tri.GetTriangle(i, out Coordinate c0, out Coordinate c1, out Coordinate c2);

                        c0.X = Math.Round(c0.X + buildingBounds.MinX, 2);
                        c0.Y = Math.Round(c0.Y + buildingBounds.MinY, 2);
                        c1.X = Math.Round(c1.X + buildingBounds.MinX, 2);
                        c1.Y = Math.Round(c1.Y + buildingBounds.MinY, 2);
                        c2.X = Math.Round(c2.X + buildingBounds.MinX, 2);
                        c2.Y = Math.Round(c2.Y + buildingBounds.MinY, 2);

                        // Skip extra segments on concave corners
                        Point center = new((c0.X + c1.X + c2.X) / 3, (c0.Y + c1.Y + c2.Y) / 3);
                        if (false == partialBuilding.Contains(center))
                        {
                            continue;
                        }

                        WriteRoofTriangle(streamWriter, fBuildingHeight, c0, c1, c2);
                    }

                    // Add building boundaries
                    WriteBuildingPolygon(tile, streamWriter, partialBuilding.ExteriorRing, fLowestGroundHeight);

                    // Interior rings (holes) are not yet supported
                }
            }

            if (iDefaultHeightCount > 0)
            {
                Logger.LogInfo($"Tile {tile.Name}: {iDefaultHeightCount} building parts defaulted to height {_fDefaultBuildingHeight}.");
            }

            streamWriter.Close();
            File.Move(sOutputTempName, sOutputFilename);

            BuildingsReader reader = new();
            return reader.Build(tile);
        }

        private static void AddPointsAlongPolygon(LineString ls, Envelope boundsRounded, ITriangulation tri)
        {
            for (int i = 1; i < ls.NumPoints; i++)
            {
                Coordinate c0 = ls.GetCoordinateN(i - 1);
                Coordinate c1 = ls.GetCoordinateN(i);

                // Scale the coordinates because Line handles only integers. This way we get better accuracy instead of ~meter 
                const int iS = 100;
                int iX1 = (int)(c0.X * iS);
                int iY1 = (int)(c0.Y * iS);
                int iX2 = (int)(c1.X * iS);
                int iY2 = (int)(c1.Y * iS);

                // Add start point
                tri.AddPoint(new LasPoint() { x = c0.X - boundsRounded.MinX, y = c0.Y - boundsRounded.MinY });

                int iCount = 0;

                foreach ((int iX, int iY) in LasUtility.Common.MathUtils.Line(iX1, iY1, iX2, iY2))
                {
                    iCount++;

                    // Line functions returns points on higher detail grid. Take only every every iS to get more like ~meter
                    if (iCount % iS != 0)
                        continue;

                    tri.AddPoint(new LasPoint()
                    {
                        // iS causes coordinate rouding and the result can be max 0.009 negative. Use Math.Max to get to 0.0.
                        x = Math.Max(0.0, (double)iX / iS - boundsRounded.MinX),
                        y = Math.Max(0.0, (double)iY / iS - boundsRounded.MinY)
                    });
                }
            }

            // No need to add end point since the polygon starts and ends at the same coordinate
        }

        private static void WriteRoofTriangle(StreamWriter streamWriter, float fBuildingHeight, Coordinate c0, Coordinate c1, Coordinate c2)
        {
            // Start polygon
            streamWriter.Write("{ \"type\":\"Polygon\", \"coordinates\": ");
            streamWriter.Write("[[");

            streamWriter.Write($"[{c0.X},{c0.Y},{fBuildingHeight}],");
            streamWriter.Write($"[{c1.X},{c1.Y},{fBuildingHeight}],");
            streamWriter.Write($"[{c2.X},{c2.Y},{fBuildingHeight}],");
            streamWriter.Write($"[{c0.X},{c0.Y},{fBuildingHeight}]");

            // End polygon
            streamWriter.Write("]]");
            streamWriter.WriteLine("},");
        }

        private static void WriteBuildingPolygon(Tile tile, StreamWriter streamWriter, LineString buildingExterior, float fLowestGroundHeight)
        {
            streamWriter.Write("{ \"type\":\"Polygon\", \"coordinates\": ");
            streamWriter.Write("[[");

            for (int i = 0; i < buildingExterior.Coordinates.Length; i++)
            {
                Coordinate c = buildingExterior.Coordinates[i];

                double dGroundHeight = tile.DemDsm.GetValue(c);

                if (double.IsNaN(dGroundHeight))
                    dGroundHeight = fLowestGroundHeight;

                streamWriter.Write($"[{Math.Round(c.X, 2)},{Math.Round(c.Y, 2)},{Math.Round(dGroundHeight, 2)}]");

                if (i < buildingExterior.Coordinates.Length - 1)
                {
                    streamWriter.Write(",");
                }
            }

            // End polygon
            streamWriter.Write("]]");
            streamWriter.WriteLine("},");

            // End geometry collection
            streamWriter.WriteLine("]}");
        }
    }
}
