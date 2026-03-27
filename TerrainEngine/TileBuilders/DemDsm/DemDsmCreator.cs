using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using LasUtility.DEM;
using LasUtility.LAS;
using LasUtility.Nls;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Kuoste.TerrainEngine.TileBuilders.DemDsm
{
    public class DemDsmCreator : Builder, IDemDsmBuilder
    {
        /// <summary>
        /// Use some overlap in triangulations or else the triangulations won't be complete on edges
        /// </summary>
        const int _iOverlapInMeters = (_iTotalEdgeLengthInMeters - TileCommon.EdgeLength) / 2;

        /// <summary>
        /// Total triangulation edge length
        /// </summary>
        const int _iTotalEdgeLengthInMeters = 1084;

        const int _iTotalEdgeLengthInPixels = 1110;

        /// <summary>
        /// Keep track of the las files so that we don't try to process the same tile multiple times.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _3kmDemDsmDone = new();

        public VoxelGrid Build(Tile tile)
        {
            if (IsCancellationRequested())
                return new();

            TileNamer.Decode(tile.Name, out Envelope bounds1km);
            string s3km3kmTileName = TileNamer.Encode((int)bounds1km.MinX, (int)bounds1km.MinY, 3000);
            TileNamer.Decode(s3km3kmTileName, out Envelope bounds3km);

            // Check if the tile is already being processed
            if (true == _3kmDemDsmDone.TryGetValue(s3km3kmTileName, out bool bIsCompleted))
            {
                if (bIsCompleted)
                {
                    // Las file is already processed, so just update the tile.
                    Logger.LogInfo($"DemAndDsmPointCloud for {tile.Name} is already completed.");
                    return VoxelGrid.Deserialize(Path.Combine(tile.Common.DirectoryIntermediate, IDemDsmBuilder.Filename(tile.Name, tile.Common.Version)));
                }
                else
                {
                    Logger.LogInfo($"DemAndDsmPointCloud for {tile.Name} is under work.");
                    return new();
                }
            }

            _3kmDemDsmDone.TryAdd(s3km3kmTileName, false);

            ILasFileReader reader = new LasZipNetReader();

            string sFilename = Path.Combine(tile.Common.DirectoryOriginal, s3km3kmTileName + ".laz");

            reader.ReadHeader(sFilename);

            Stopwatch sw = Stopwatch.StartNew();

            reader.OpenReader(sFilename);

            int iSubmeshesPerEdge = (int)Math.Round((reader.MaxX - reader.MinX) / TileCommon.EdgeLength);
            int iSubmeshCount = (int)Math.Pow(iSubmeshesPerEdge, 2);

            SurfaceTriangulation[] triangulations = new SurfaceTriangulation[iSubmeshCount];
            VoxelGrid[] grids = new VoxelGrid[iSubmeshCount];
            List<bool[,]> lockedCells = new();

            for (int i = 0; i < iSubmeshCount; i++)
            {
                // Create the NLS (Maanmittauslaitos) style name of a 1x1 km2 tile in order to get the coordinates.
                string sSubmeshName = s3km3kmTileName + "_" + (i + 1).ToString();
                TileNamer.Decode(sSubmeshName, out Envelope extent);

                extent.ExpandBy(_iOverlapInMeters);

                grids[i] = VoxelGrid.CreateGrid(_iTotalEdgeLengthInPixels, _iTotalEdgeLengthInPixels, extent);

                triangulations[i] = new SurfaceTriangulation(_iTotalEdgeLengthInMeters, _iTotalEdgeLengthInMeters,
                    extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);

                lockedCells.Add(new bool[_iTotalEdgeLengthInPixels, _iTotalEdgeLengthInPixels]);
            }

            foreach (LasPoint p in reader.Points())
            {
                if (IsCancellationRequested())
                    return new();

                if (p.classification != (byte)PointCloud05p.Classes.LowVegetation &&
                    p.classification != (byte)PointCloud05p.Classes.MedVegetation &&
                    p.classification != (byte)PointCloud05p.Classes.HighVegetation &&
                    p.classification != (byte)PointCloud05p.Classes.Ground)
                {
                    continue;
                }

                // Move coordinates to 0
                int i3kmX = (int)(p.x - bounds3km.MinX);
                int i3kmY = (int)(p.y - bounds3km.MinY);

                AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX, i3kmY);

                // Look if point is part of another submesh overlap area.
                // Overlap is needed because otherwise adjacent triangulated surfaces have a gap in between.

                int iTileX = i3kmX % TileCommon.EdgeLength;
                int iTileY = i3kmY % TileCommon.EdgeLength;

                int iLowerBound = _iOverlapInMeters;
                int iUpperBound = TileCommon.EdgeLength - _iOverlapInMeters;
                int iMoveBy = _iOverlapInMeters;

                if (iTileX < iLowerBound || iTileX > iUpperBound || iTileY < iLowerBound || iTileY > iUpperBound)
                {
                    // This point also belongs to an overlap area of one or more other submesh.

                    int iWholeMeshEdgeLength = TileCommon.EdgeLength * iSubmeshesPerEdge;

                    if (i3kmX < iLowerBound || i3kmX > iWholeMeshEdgeLength - iUpperBound ||
                        i3kmY < iLowerBound || i3kmY > iWholeMeshEdgeLength - iUpperBound)
                    {
                        // Part of another file. Todo: Save these points to four separate files
                        // so they can be read when adjacent laz files are processed.

                        continue;
                    }

                    if (iTileX < iLowerBound)
                    {
                        // West
                        AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX - iMoveBy, i3kmY);

                        if (iTileY < iLowerBound)
                        {
                            // Southwest
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX - iMoveBy, i3kmY - iMoveBy);

                            // South
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX, i3kmY - iMoveBy);
                        }
                        else if (iTileY > iUpperBound)
                        {
                            // Northwest
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX - iMoveBy, i3kmY + iMoveBy);

                            // North
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX, i3kmY + iMoveBy);
                        }
                    }

                    if (iTileX > iUpperBound)
                    {
                        // East
                        AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX + iMoveBy, i3kmY);

                        if (iTileY < iLowerBound)
                        {
                            // Southeast
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX + iMoveBy, i3kmY - iMoveBy);

                            // South
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX, i3kmY - iMoveBy);
                        }
                        else if (iTileY > iUpperBound)
                        {
                            // Northeast
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX + iMoveBy, i3kmY + iMoveBy);

                            // North
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX, i3kmY + iMoveBy);
                        }
                    }

                    if (iTileY < iLowerBound)
                    {
                        // South
                        AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX, i3kmY - iMoveBy);

                        if (iTileX < iLowerBound)
                        {
                            // Southwest
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX - iMoveBy, i3kmY - iMoveBy);

                            // West
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX - iMoveBy, i3kmY);
                        }
                        else if (iTileX > iUpperBound)
                        {
                            // Southeast
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX + iMoveBy, i3kmY - iMoveBy);

                            // East
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX + iMoveBy, i3kmY);
                        }
                    }

                    if (iTileY > iUpperBound)
                    {
                        // North
                        AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX, i3kmY + iMoveBy);

                        if (iTileX < iLowerBound)
                        {
                            // Northwest
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX - iMoveBy, i3kmY + iMoveBy);

                            // West
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX - iMoveBy, i3kmY);
                        }
                        else if (iTileX > iUpperBound)
                        {
                            // Northeast
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX + iMoveBy, i3kmY + iMoveBy);

                            // East
                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, lockedCells, i3kmX + iMoveBy, i3kmY);
                        }
                    }
                }
            }

            reader.CloseReader();

            sw.Stop();
            Logger.LogDebug($"Tile {s3km3kmTileName} was read to grid and triangulations in {sw.Elapsed.TotalSeconds} seconds.");

            for (int i = 0; i < iSubmeshCount; i++)
            {
                if (IsCancellationRequested())
                    return new();

                Stopwatch sw2 = Stopwatch.StartNew();

                // Use the name of a 1x1 km2 tile to get the coordinates
                string sSubmeshName = s3km3kmTileName + "_" + (i + 1).ToString();
                TileNamer.Decode(sSubmeshName, out Envelope env);

                SurfaceTriangulation tri = triangulations[i];
                VoxelGrid grid = grids[i];

                if (tri.PointCount < 10)
                {
                    Logger.LogWarning($"Not enough points for triangulating {sSubmeshName}");
                    continue;
                }

                grid.SortAndTrim();

                tri.Create();


                // Cannot use full overlap because triangulation is not complete on edges
                env.ExpandBy(_iOverlapInMeters / 2);

                grid.SetMissingHeightsFromTriangulation(tri,
                    (int)env.MinX, (int)env.MinY, (int)env.MaxX, (int)env.MaxY,
                    out int iMissBefore, out int iMissAfter);

                RasteriseDemRequest request = new(grid.Dem, grid.Bounds);
                request.LockedCells = lockedCells[i];
                tri.RasteriseDem(request);

                // Free triangulation asap so we dont run out of memory.
                tri.Clear();

                sw2.Stop();

                Logger.LogDebug($"Triangulating {sSubmeshName} took {sw2.Elapsed.TotalSeconds} s. " +
                    $"Empty cells before {iMissBefore}, after {iMissAfter}.");
            }

            VoxelGrid output = new();

            for (int i = 0; i < iSubmeshCount; i++)
            {
                if (IsCancellationRequested())
                    return new();

                string s1km1kmTilename = s3km3kmTileName + "_" + (i + 1).ToString();

                // Save grid to filesystem for future use
                grids[i].Serialize(Path.Combine(tile.Common.DirectoryIntermediate, IDemDsmBuilder.Filename(s1km1kmTilename, tile.Common.Version)));

                //grids[i].WriteDemAsAscii(Path.Combine(tile.DirectoryIntermediate, s1km1kmTilename + ".asc"));

                if (tile.Name == s1km1kmTilename)
                {
                    output = grids[i];
                }
            }

            //sw.Stop();
            //Debug.Log($"Las processing finished! Total time for tile {s3km3kmTileName} was {sw.Elapsed.TotalSeconds} seconds.");

            _3kmDemDsmDone.TryUpdate(s3km3kmTileName, true, false);
            return output;

        }

        private static void AddPoint(
            LasPoint p, 
            int iSubmeshesPerEdge,
            ITriangulation[] triangulations, 
            VoxelGrid[] grids,
            List<bool[,]> lockedCells,
            int x, 
            int y)
        {
            int ix = x / TileCommon.EdgeLength;
            int iy = y / TileCommon.EdgeLength;

            if (ix < 0 || ix >= iSubmeshesPerEdge || iy < 0 || iy >= iSubmeshesPerEdge)
            {
                throw new Exception($"Coordinates of a point (x={p.x}, y={p.y} are out of bounds");
            }

            int iSubmeshIndex = ix * iSubmeshesPerEdge + iy;
            bool bIsGround = p.classification == (byte)PointCloud05p.Classes.Ground;

            grids[iSubmeshIndex].AddPoint(p.x, p.y, (float)p.z, p.classification, bIsGround);

            if (bIsGround)
            {
                triangulations[iSubmeshIndex].AddPoint(p);
                grids[iSubmeshIndex].GetGridIndexes(p.x, p.y, out int iRow, out int iCol);
                lockedCells[iSubmeshIndex][iRow, iCol] = true;
            }
        }
    }
}
