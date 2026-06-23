using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using NetTopologySuite.Geometries;
using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Kuoste.TerrainEngine.TileBuilders.Trees
{
    public class TreeReader : Builder, ITreeBuilder
    {
        public List<Point> Build(Tile tile)
        {
            List<Point> trees = new();

            if (IsCancellationRequested())
                return trees;

            Envelope bounds = tile.Common.TileScheme.Decode(tile.Name);
            string sFullFilename = Path.Combine(tile.Common.DirectoryIntermediate, ITreeBuilder.Filename(tile.Name, tile.Common.Version));

            string[] sTrees = File.ReadAllText(sFullFilename).Split("Point");

            foreach (string sTree in sTrees)
            {
                if (IsCancellationRequested())
                    return trees;

                string[] sCordinates = sTree.Split("[", StringSplitOptions.RemoveEmptyEntries);

                foreach (string sCoordinate in sCordinates)
                {
                    if (!char.IsDigit(sCoordinate[0]))
                        continue;

                    var coords = sCoordinate.Split(",", StringSplitOptions.RemoveEmptyEntries);

                    // Delete the last character which is a closing bracket and everyting after it
                    coords[2] = coords[2][..coords[2].IndexOf(']')];

                    trees.Add(new(
                        (double.Parse(coords[0], CultureInfo.InvariantCulture) - bounds.MinX) / tile.Common.OutputEdgeLength,
                        (double.Parse(coords[1], CultureInfo.InvariantCulture) - bounds.MinY) / tile.Common.OutputEdgeLength,
                        double.Parse(coords[2], CultureInfo.InvariantCulture)));
                }
            }

            return trees;

        }
    }

}
