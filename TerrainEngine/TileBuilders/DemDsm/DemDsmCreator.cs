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

namespace Kuoste.TerrainEngine.TileBuilders.DemDsm
{
    public class DemDsmCreator : Builder, IDemDsmBuilder
    {
        /// <summary>
        /// Overlap (m) added around each output grid and triangulation block so adjacent blocks /
        /// output tiles don't gap on their shared edges. 42 m for the historical 1084 m / 1000 m grids.
        /// </summary>
        const int _iOverlapInMeters = (_iTotalEdgeLengthInMeters - 1000) / 2;

        /// <summary>Edge length (m) of a 1 km output grid including overlap.</summary>
        const int _iTotalEdgeLengthInMeters = 1084;

        /// <summary>Pixel resolution of a 1 km output grid (incl. overlap).</summary>
        const int _iTotalEdgeLengthInPixels = 1110;

        /// <summary>
        /// Keep track of the source las files so we don't process the same source tile multiple times.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _sourceDemDsmDone = new();

        public VoxelGrid Build(Tile tile)
        {
            if (IsCancellationRequested())
                return new();

            int iOutputEdge = tile.Common.OutputEdgeLength;

            Envelope boundsOutput = tile.Common.TileScheme.Decode(tile.Name);
            string sSourceTileName = tile.Common.TileScheme.Encode(boundsOutput.MinX, boundsOutput.MinY, tile.Common.SourceEdgeLength);
            Envelope boundsSource = tile.Common.TileScheme.Decode(sSourceTileName);

            // Check if the source tile is already being processed
            if (true == _sourceDemDsmDone.TryGetValue(sSourceTileName, out bool bIsCompleted))
            {
                if (bIsCompleted)
                {
                    Logger.LogInfo($"DemAndDsmPointCloud for {tile.Name} is already completed.");
                    return VoxelGrid.Deserialize(Path.Combine(tile.Common.DirectoryIntermediate, IDemDsmBuilder.Filename(tile.Name, tile.Common.Version)));
                }
                else
                {
                    Logger.LogInfo($"DemAndDsmPointCloud for {tile.Name} is under work.");
                    return new();
                }
            }

            _sourceDemDsmDone.TryAdd(sSourceTileName, false);

            ILasFileReader reader = new LasZipNetReader();
            string sFilename = Path.Combine(tile.Common.DirectoryOriginal, sSourceTileName + ".laz");
            reader.ReadHeader(sFilename);

            Stopwatch sw = Stopwatch.StartNew();
            reader.OpenReader(sFilename);

            // NLS delivers full 1 or 3 km tiles even on the coast, but the .laz header extent only
            // spans the actual points, so the grid layout must come from the nominal tile bounds.
            int iSourceEdge = (int)Math.Round(boundsSource.Width);
            int iSourceMinX = (int)Math.Round(boundsSource.MinX);
            int iSourceMinY = (int)Math.Round(boundsSource.MinY);

            double dPointExtent = Math.Max(reader.MaxX - reader.MinX, reader.MaxY - reader.MinY);
            if (Math.Abs(iSourceEdge - dPointExtent) > 1)
            {
                Logger.LogWarning($"Source {sSourceTileName}: point extent {dPointExtent:F0} m does not match " +
                    $"the nominal {iSourceEdge} m tile (partial coverage or mis-tiled file). Sparse or empty outputs possible.");
            }

            // --- Output grids: one overlapping output-size grid per output tile in the source (always). ---
            int iTilesPerEdge = iSourceEdge / iOutputEdge;
            int iTileCount = iTilesPerEdge * iTilesPerEdge;

            VoxelGrid[] grids = new VoxelGrid[iTileCount];
            Envelope[] gridExtents = new Envelope[iTileCount];   // with overlap — for point distribution
            Envelope[] gridCores = new Envelope[iTileCount];     // without overlap — the rendered tile
            string[] gridNames = new string[iTileCount];
            List<bool[,]> lockedCells = new();

            for (int ty = 0; ty < iTilesPerEdge; ty++)
            {
                for (int tx = 0; tx < iTilesPerEdge; tx++)
                {
                    int i = ty * iTilesPerEdge + tx;
                    int tileMinX = iSourceMinX + tx * iOutputEdge;
                    int tileMinY = iSourceMinY + ty * iOutputEdge;

                    gridCores[i] = new Envelope(tileMinX, tileMinX + iOutputEdge, tileMinY, tileMinY + iOutputEdge);
                    gridNames[i] = tile.Common.TileScheme.Encode(tileMinX + iOutputEdge / 2, tileMinY + iOutputEdge / 2, iOutputEdge);

                    Envelope extent = new(gridCores[i]);
                    extent.ExpandBy(_iOverlapInMeters);
                    gridExtents[i] = extent;

                    grids[i] = VoxelGrid.CreateGrid(_iTotalEdgeLengthInPixels, _iTotalEdgeLengthInPixels, extent);
                    lockedCells.Add(new bool[_iTotalEdgeLengthInPixels, _iTotalEdgeLengthInPixels]);
                }
            }

            // --- Triangulation blocks: the whole source as one block when BlockEdgeLength >= source
            //     (no internal seams), else one block per output tile (the cheap low-mem path). ---
            bool bWholeBlock = tile.Common.BlockEdgeLength >= iSourceEdge;
            int iBlockEdge = bWholeBlock ? iSourceEdge : iOutputEdge;
            int iBlocksPerEdge = iSourceEdge / iBlockEdge;
            int iBlockCount = iBlocksPerEdge * iBlocksPerEdge;

            Logger.LogInfo($"Building source {sSourceTileName} ({iSourceEdge} m): {iTileCount} output tile(s) of {iOutputEdge} m, " +
                $"{iBlockCount} triangulation block(s) of {iBlockEdge} m ({(bWholeBlock ? "whole-block" : "submesh")}), SeamMode {tile.Common.SeamMode}.");

            SurfaceTriangulation[] triangulations = new SurfaceTriangulation[iBlockCount];
            Envelope[] blockCores = new Envelope[iBlockCount];    // without overlap — for grid -> block mapping
            Envelope[] blockExtents = new Envelope[iBlockCount];  // with overlap — for point distribution

            for (int b = 0; b < iBlockCount; b++)
            {
                int bx = b / iBlocksPerEdge;
                int by = b % iBlocksPerEdge;
                int blockMinX = iSourceMinX + bx * iBlockEdge;
                int blockMinY = iSourceMinY + by * iBlockEdge;

                blockCores[b] = new Envelope(blockMinX, blockMinX + iBlockEdge, blockMinY, blockMinY + iBlockEdge);

                Envelope extent = new(
                    blockMinX - _iOverlapInMeters, blockMinX + iBlockEdge + _iOverlapInMeters,
                    blockMinY - _iOverlapInMeters, blockMinY + iBlockEdge + _iOverlapInMeters);
                blockExtents[b] = extent;

                triangulations[b] = new SurfaceTriangulation(
                    (int)Math.Round(extent.Width), (int)Math.Round(extent.Height),
                    extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);
            }

            // Map each output grid to the block whose core contains its tile centre.
            int[] gridBlock = new int[iTileCount];
            for (int i = 0; i < iTileCount; i++)
            {
                Coordinate c = gridCores[i].Centre;
                for (int b = 0; b < iBlockCount; b++)
                {
                    if (blockCores[b].Contains(c.X, c.Y)) { gridBlock[i] = b; break; }
                }
            }

            // --- Distribute a point by bounding box (replaces the per-submesh overlap juggling). ---
            // Used for the source's own points and for neighbour halo-band points alike.
            void Distribute(LasPoint p)
            {
                if (p.classification != (byte)PointCloud05p.Classes.LowVegetation &&
                    p.classification != (byte)PointCloud05p.Classes.MedVegetation &&
                    p.classification != (byte)PointCloud05p.Classes.HighVegetation &&
                    p.classification != (byte)PointCloud05p.Classes.Ground)
                {
                    return;
                }

                bool bIsGround = p.classification == (byte)PointCloud05p.Classes.Ground;

                // Add to every output grid whose (overlapping) extent contains the point.
                for (int i = 0; i < iTileCount; i++)
                {
                    Envelope e = gridExtents[i];
                    if (p.x >= e.MinX && p.x < e.MaxX && p.y >= e.MinY && p.y < e.MaxY)
                    {
                        grids[i].AddPoint(p.x, p.y, (float)p.z, p.classification, bIsGround);

                        if (bIsGround)
                        {
                            grids[i].GetGridIndexes(p.x, p.y, out int iRow, out int iCol);
                            lockedCells[i][iRow, iCol] = true;
                        }
                    }
                }

                // Ground points feed every block triangulation whose extent strictly contains them.
                if (bIsGround)
                {
                    for (int b = 0; b < iBlockCount; b++)
                    {
                        Envelope e = blockExtents[b];
                        if (p.x > e.MinX && p.x < e.MaxX && p.y > e.MinY && p.y < e.MaxY)
                            triangulations[b].AddPoint(p);
                    }
                }
            }

            bool bSeam = tile.Common.SeamMode != SeamMode.None;
            int iSourceMaxX = iSourceMinX + iSourceEdge;
            int iSourceMaxY = iSourceMinY + iSourceEdge;

            // Ground points within the overlap of any source edge form this tile's halo frame,
            // shared with neighbouring source tiles to close the cross-source seam.
            List<LasPoint> ownFrame = new();

            foreach (LasPoint p in reader.Points())
            {
                if (IsCancellationRequested())
                    return new();

                Distribute(p);

                if (bSeam && p.classification == (byte)PointCloud05p.Classes.Ground &&
                    (p.x < iSourceMinX + _iOverlapInMeters || p.x >= iSourceMaxX - _iOverlapInMeters ||
                     p.y < iSourceMinY + _iOverlapInMeters || p.y >= iSourceMaxY - _iOverlapInMeters))
                {
                    ownFrame.Add(p);
                }
            }

            reader.CloseReader();
            sw.Stop();
            Logger.LogDebug($"Source {sSourceTileName} read into {iTileCount} grids and {iBlockCount} block triangulation(s) in {sw.Elapsed.TotalSeconds} s.");

            // --- Cross-source seam halo: write this source's frame, consume neighbours' frames. ---
            if (bSeam)
            {
                string sOwnBand = Path.Combine(tile.Common.DirectoryIntermediate, IHaloBuilder.Filename(sSourceTileName, tile.Common.Version));
                HaloBandWriter.Write(sFilename, sOwnBand, ownFrame, boundsSource);
                Logger.LogInfo($"Halo [{tile.Common.SeamMode}]: wrote {ownFrame.Count} ground points for {sSourceTileName} -> {Path.GetFileName(sOwnBand)}");

                int iNeighboursConsumed = 0;

                foreach (string sNeighbour in tile.Common.TileScheme.Neighbors(sSourceTileName))
                {
                    if (IsCancellationRequested())
                        return new();

                    string sNeighbourBand = Path.Combine(tile.Common.DirectoryIntermediate, IHaloBuilder.Filename(sNeighbour, tile.Common.Version));

                    if (false == File.Exists(sNeighbourBand))
                    {
                        // SinglePass only consumes halos that already exist; TwoPass extracts a
                        // missing neighbour's halo on demand so build order doesn't matter.
                        if (tile.Common.SeamMode != SeamMode.TwoPass)
                        {
                            Logger.LogDebug($"Halo: no band for neighbour {sNeighbour}; skipping ({tile.Common.SeamMode}).");
                            continue;
                        }

                        string sNeighbourLaz = Path.Combine(tile.Common.DirectoryOriginal, sNeighbour + ".laz");
                        if (false == File.Exists(sNeighbourLaz))
                        {
                            Logger.LogDebug($"Halo: neighbour {sNeighbour} has neither band nor source .laz; skipping.");
                            continue;
                        }

                        HaloBandWriter.ExtractFrameToBand(sNeighbourLaz, tile.Common.TileScheme.Decode(sNeighbour), _iOverlapInMeters, sNeighbourBand);
                        Logger.LogInfo($"Halo [TwoPass]: extracted missing band for neighbour {sNeighbour} on demand -> {Path.GetFileName(sNeighbourBand)}");
                    }

                    ILasFileReader bandReader = new LasZipNetReader();
                    bandReader.ReadHeader(sNeighbourBand);
                    bandReader.OpenReader(sNeighbourBand);

                    int iPointsFromNeighbour = 0;
                    foreach (LasPoint bp in bandReader.Points())
                    {
                        if (IsCancellationRequested())
                            return new();

                        Distribute(bp);
                        iPointsFromNeighbour++;
                    }

                    bandReader.CloseReader();
                    iNeighboursConsumed++;
                    Logger.LogInfo($"Halo: {sSourceTileName} consumed {iPointsFromNeighbour} points from neighbour {sNeighbour}.");
                }

                Logger.LogInfo($"Halo: {sSourceTileName} consumed halos from {iNeighboursConsumed} neighbour(s).");
            }

            // --- Triangulate each block, rasterise its output grids, then free it (low peak memory). ---
            for (int b = 0; b < iBlockCount; b++)
            {
                if (IsCancellationRequested())
                    return new();

                SurfaceTriangulation tri = triangulations[b];

                if (tri.PointCount < 10)
                {
                    Logger.LogWarning($"Not enough points for triangulating block {b} of {sSourceTileName}");
                    tri.Clear();
                    continue;
                }

                Stopwatch sw2 = Stopwatch.StartNew();
                tri.Create();

                for (int i = 0; i < iTileCount; i++)
                {
                    if (gridBlock[i] != b)
                        continue;

                    Envelope env = new(gridCores[i]);

                    grids[i].SortAndTrim();

                    // Cannot use full overlap because triangulation is not complete on edges
                    env.ExpandBy(_iOverlapInMeters / 2);

                    grids[i].SetMissingHeightsFromTriangulation(tri,
                        (int)env.MinX, (int)env.MinY, (int)env.MaxX, (int)env.MaxY,
                        out int iMissBefore, out int iMissAfter);

                    RasteriseDemRequest request = new(grids[i].Dem, grids[i].Bounds);
                    request.LockedCells = lockedCells[i];
                    tri.RasteriseDem(request);

                    Logger.LogDebug($"Rasterised {gridNames[i]} from block {b}. Empty cells {iMissBefore} -> {iMissAfter}.");
                }

                tri.Clear();
                sw2.Stop();
                Logger.LogDebug($"Block {b} of {sSourceTileName} triangulated in {sw2.Elapsed.TotalSeconds} s.");
            }

            // --- Serialize all output grids. ---
            VoxelGrid output = new();
            for (int i = 0; i < iTileCount; i++)
            {
                if (IsCancellationRequested())
                    return new();

                grids[i].Serialize(Path.Combine(tile.Common.DirectoryIntermediate, IDemDsmBuilder.Filename(gridNames[i], tile.Common.Version)));

                if (tile.Name == gridNames[i])
                    output = grids[i];
            }

            _sourceDemDsmDone.TryUpdate(sSourceTileName, true, false);
            return output;
        }
    }
}
