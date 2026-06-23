using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using LasUtility.Nls;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace Kuoste.TerrainEngine.TileBuilders.Buildings
{
    public class BuildingsReader : Builder, IBuildingsBuilder
    {
        public List<Tile.Building> Build(Tile tile)
        {
            List<Tile.Building> buildings = new();

            if (IsCancellationRequested())
                return buildings;

            TileNamer.Decode(tile.Name, out Envelope bounds);
            string sFullFilename = Path.Combine(tile.Common.DirectoryIntermediate, IBuildingsBuilder.Filename(tile.Name, tile.Common.Version));

            string[] sBuildings = File.ReadAllText(sFullFilename).Split("GeometryCollection");

            foreach (string sBuilding in sBuildings)
            {
                if (IsCancellationRequested())
                    return buildings;

                List<Vector3> buildingVertices = new();
                List<int> buildingTriangles = new();
                int iStartingTriangleIndexForWalls = 0;

                float fWallHeight = 0.0f;

                string[] sPolygons = sBuilding.Split("Polygon");

                for (int i = 0; i < sPolygons.Length; i++)
                {
                    List<Vector3> vCoordinates = new();
                    string[] sCordinates = sPolygons[i].Split("[", StringSplitOptions.RemoveEmptyEntries);

                    foreach (string sCoordinate in sCordinates)
                    {
                        if (!char.IsDigit(sCoordinate[0]))
                            continue;

                        string[] sCoords = sCoordinate.Split(",", StringSplitOptions.RemoveEmptyEntries);

                        // Delete the last character which is a closing bracket and everyting after it
                        sCoords[2] = sCoords[2][..sCoords[2].IndexOf(']')];

                        vCoordinates.Add(new(
                            float.Parse(sCoords[0], CultureInfo.InvariantCulture),
                            float.Parse(sCoords[1], CultureInfo.InvariantCulture),
                            float.Parse(sCoords[2], CultureInfo.InvariantCulture)));
                    }

                    if (vCoordinates.Count == 0)
                        continue;

                    if (i == sPolygons.Length - 1)
                    {
                        // Last polygon is walls

                        iStartingTriangleIndexForWalls = buildingTriangles.Count;

                        // Add wall vertices
                        for (int c = 1; c < vCoordinates.Count; c++)
                        {
                            Vector3 c1 = vCoordinates[c];
                            Vector3 c0 = vCoordinates[c - 1];

                            // Create a quad between the two points
                            int iVertexStart = buildingVertices.Count;
                            buildingVertices.Add(new Vector3((float)(c0.X - bounds.MinX), (float)(c0.Y - bounds.MinY), c0.Z));
                            buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX), (float)(c1.Y - bounds.MinY), c1.Z));
                            buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX), (float)(c1.Y - bounds.MinY), fWallHeight));
                            buildingVertices.Add(new Vector3((float)(c0.X - bounds.MinX), (float)(c0.Y - bounds.MinY), fWallHeight));

                            buildingTriangles.Add(iVertexStart);
                            buildingTriangles.Add(iVertexStart + 1);
                            buildingTriangles.Add(iVertexStart + 2);
                            buildingTriangles.Add(iVertexStart);
                            buildingTriangles.Add(iVertexStart + 2);
                            buildingTriangles.Add(iVertexStart + 3);
                        }
                    }
                    else
                    {
                        // All polygons before walls are roof triangles

                        // Count should be 4 because the first and last coordinate are the same
                        if (vCoordinates.Count != 4)
                            throw new Exception("Invalid roof polygon format");

                        // All the heights are currently the same. Save any of them for the wall height
                        fWallHeight = vCoordinates[0].Z;

                        int iVertexStart = buildingVertices.Count;

                        buildingVertices.Add(new(
                            (float)(vCoordinates[0].X - bounds.MinX), 
                            (float)(vCoordinates[0].Y - bounds.MinY), 
                            vCoordinates[0].Z));
                        buildingVertices.Add(new(
                            (float)(vCoordinates[1].X - bounds.MinX), 
                            (float)(vCoordinates[1].Y - bounds.MinY), 
                            vCoordinates[1].Z));
                        buildingVertices.Add(new(
                            (float)(vCoordinates[2].X - bounds.MinX), 
                            (float)(vCoordinates[2].Y - bounds.MinY), 
                            vCoordinates[2].Z));

                        buildingTriangles.Add(iVertexStart);
                        buildingTriangles.Add(iVertexStart + 1);
                        buildingTriangles.Add(iVertexStart + 2);
                    }
                }

                if (iStartingTriangleIndexForWalls > 0)
                {
                    buildings.Add(new Tile.Building()
                    {
                        Vertices = buildingVertices.ToArray(),
                        Triangles = buildingTriangles.ToArray(),
                        iSubmeshSeparator = iStartingTriangleIndexForWalls,
                    });
                }
            }

            return buildings;
        }
    }
}
