using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using LasUtility.Common;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Kuoste.TerrainEngine.TileBuilders.Trees
{
    public class SimpleTreeCreator : Builder, ITreeBuilder
    {
        const int _iSearchRadiusBuildingsRoads = 3;
        const int _iSearchRadiusHighVegetation = 2;
        const int _iRequiredHighVegetationCountAroundTree = 5;

        // Use same bounds so that the older LAZ files with different classifications are treated similarly
        // New classification: https://www.maanmittauslaitos.fi/kartat-ja-paikkatieto/aineistot-ja-rajapinnat/tuotekuvaukset/laserkeilausaineisto-05-p
        // Old: https://www.maanmittauslaitos.fi/kartat-ja-paikkatieto/aineistot-ja-rajapinnat/tuotekuvaukset/laserkeilausaineisto
        const int _iHighVegetationLowerBound = 2;
        const int _iHighVegetationHigherBound = 50;

        private bool IsHighVegetation(float z) => z >= _iHighVegetationLowerBound && z <= _iHighVegetationHigherBound;

        public List<Point> Build(Tile tile)
        {
            Envelope bounds = tile.Common.TileScheme.Decode(tile.Name);

            List<Point> trees = new();

            if (IsCancellationRequested())
                return trees;

            string sOutputFilename = Path.Combine(tile.Common.DirectoryIntermediate, ITreeBuilder.Filename(tile.Name, tile.Common.Version));
            string sOutputTempName = sOutputFilename + ".tmp";
            using StreamWriter streamWriter = new(sOutputTempName);

            RcIndex start = tile.DemDsm.Bounds.ProjToCell(new Coordinate(bounds.MinX, bounds.MinY));
            RcIndex end = tile.DemDsm.Bounds.ProjToCell(new Coordinate(bounds.MaxX, bounds.MaxY));

            for (int iRow = start.Row; iRow < end.Row; iRow++)
            {
                for (int jCol = start.Column; jCol < end.Column; jCol++)
                {
                    if (IsCancellationRequested())
                    {
                        streamWriter.Close();
                        File.Delete(sOutputTempName);
                        return trees;
                    }

                    if (AreBuildingsRoadsNearby(tile, iRow, jCol, _iSearchRadiusBuildingsRoads))
                        continue;

                    List<BinPoint> centerPoints = tile.DemDsm.GetPoints(iRow, jCol);

                    if (centerPoints.Count == 0)
                    {
                        continue;
                    }

                    float fGroundHeight = (float)tile.DemDsm.GetValue(iRow, jCol);

                    // Ground height is not always available (e.g. triangulation on corners of the tile)
                    if (float.IsNaN(fGroundHeight))
                    {
                        continue;
                    }

                    float fHeightForPossibleTree = centerPoints[0].Z - fGroundHeight;

                    // Our tree candidate has to be appropriate height
                    if (false == IsHighVegetation(fHeightForPossibleTree))
                    {
                        continue;
                    }

                    float fNearbyMaxHeights = float.MinValue;
                    int iHighVegetationCount = 0;

                    for (int ii = iRow - _iSearchRadiusHighVegetation; ii <= iRow + _iSearchRadiusHighVegetation; ii++)
                    {
                        for (int jj = jCol - _iSearchRadiusHighVegetation; jj <= jCol + _iSearchRadiusHighVegetation; jj++)
                        {
                            //if (ii < 0 || ii > tile.DemDsm.Bounds.RowCount - 1 ||
                            //    jj < 0 || jj > tile.DemDsm.Bounds.ColumnCount - 1)
                            //{
                            //    continue;
                            //}

                            List<BinPoint> neighborhoodPoints = tile.DemDsm.GetPoints(ii, jj);

                            foreach (BinPoint p in neighborhoodPoints)
                            {
                                if (IsHighVegetation(p.Z - fGroundHeight))
                                {
                                    fNearbyMaxHeights = Math.Max(fNearbyMaxHeights, p.Z - fGroundHeight);

                                    iHighVegetationCount++;
                                }
                                else
                                {
                                    // Points are sorted by descending height so after high vegetation
                                    // there are no more high vegetation points
                                    break;
                                }
                            }
                        }
                    }

                    // There has to be enough high vegetation points in the neighborhood
                    // and the tree candidate has to be the highest point among them.
                    if (iHighVegetationCount >= _iRequiredHighVegetationCountAroundTree && fHeightForPossibleTree >= fNearbyMaxHeights)
                    {
                        // Write Point
                        streamWriter.Write("{\"type\":\"Point\",\"coordinates\":");
                        tile.DemDsm.GetGridCoordinates(iRow, jCol, out double x, out double y);
                        // Write as int since the accuracy is in ~meters
                        streamWriter.Write($"[{(int)x},{(int)y},{(int)fNearbyMaxHeights}]");
                        streamWriter.WriteLine("}");

                        trees.Add(new(((int)x - bounds.MinX) / TileCommon.EdgeLength, ((int)y - bounds.MinY) / TileCommon.EdgeLength, (int)fNearbyMaxHeights));
                    }
                }
            }

            //sw.Stop();
            //Debug.Log($"Tile {_tile.Name}: {_tile.Trees.Count} trees determined in {sw.ElapsedMilliseconds} ms.");
            //sw.Restart();

            streamWriter.Close();
            File.Move(sOutputTempName, sOutputFilename);

            return trees;
        }

        private bool AreBuildingsRoadsNearby(Tile tile, int iRow, int jCol, int iRadius)
        {
            if (AreBuildingsRoadsOnCell(tile, iRow, jCol))
                return true;

            if (iRow - iRadius > 0)
            {
                if (AreBuildingsRoadsOnCell(tile, iRow - iRadius, jCol))
                    return true;
            }

            if (iRow + iRadius < tile.DemDsm.Bounds.RowCount)
            {
                if (AreBuildingsRoadsOnCell(tile, iRow + iRadius, jCol))
                    return true;
            }

            if (jCol - iRadius > 0)
            {
                if (AreBuildingsRoadsOnCell(tile, iRow, jCol - iRadius))
                    return true;
            }

            if (jCol + iRadius < tile.DemDsm.Bounds.ColumnCount)
            {
                if (AreBuildingsRoadsOnCell(tile, iRow, jCol + iRadius))
                    return true;
            }

            return false;
        }

        private bool AreBuildingsRoadsOnCell(Tile tile, int iRow, int jCol)
        {
            tile.DemDsm.GetGridCoordinates(iRow, jCol, out double x, out double y);

            if (tile.BuildingsRoads.GetValue(new Coordinate(x, y)) > 0)
                return true;

            return false;
        }
    }
}
