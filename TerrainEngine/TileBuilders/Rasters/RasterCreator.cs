using LasUtility.Common;
using LasUtility.Nls;
using LasUtility.Shapefile;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri.Shapefiles.Readers;
using NetTopologySuite.IO.Esri;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;

namespace Kuoste.TerrainEngine.TileBuilders.Rasters
{
    public class RasterCreator : Builder, IRasterBuilder
    {
        private string _sRasterFilenameSpecifier;
        private string[] _sShpFilenames;

        private Dictionary<int, byte> _nlsClassesToRasterValues = new();

        public void SetRasterizedClassesWithRasterValues(Dictionary<int, byte> classesToRasterValues)
        {
            _nlsClassesToRasterValues = classesToRasterValues;
        }

        public IRaster Build(Tile tile)
        {
            if (IsCancellationRequested())
                return new ByteRaster();

            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);
            TileNamer.Decode(s12km12kmMapTileName, out Envelope bounds12km);

            string sFullFilename = Path.Combine(tile.Common.DirectoryIntermediate, IRasterBuilder.Filename(tile.Name, _sRasterFilenameSpecifier, tile.Common.Version));

            // Check if the tile is already being processed and add it to the dictionary if not.
            if (true == File.Exists(sFullFilename))
            {
                // Shapefile is already processed, so just update the tile.
                Logger.LogInfo($"TerrainTypeRaster {s12km12kmMapTileName} for {tile.Name} was already completed.");
                return ByteRaster.CreateFromAscii(sFullFilename);
            }

            RasteriserEvenOdd rasteriser = new();
            rasteriser.SetCancellationToken(CancellationToken);

            Envelope rasterBounds = new(bounds12km);

            int iRowAndColCount = TopographicDb.iMapTileEdgeLengthInMeters / TileCommon.EdgeLength * tile.Common.AlphamapResolution;
            rasteriser.InitializeRaster(iRowAndColCount, iRowAndColCount, rasterBounds);

            rasteriser.AddRasterizedClassesWithRasterValues(_nlsClassesToRasterValues);

            foreach (string sFilename in _sShpFilenames)
            {
                rasteriser.RasteriseShapefile(Path.Combine(tile.Common.DirectoryOriginal, sFilename));
            }

            for (int x = (int)bounds12km.MinX; x < (int)bounds12km.MaxX; x += TileCommon.EdgeLength)
            {
                for (int y = (int)bounds12km.MinY; y < (int)bounds12km.MaxY; y += TileCommon.EdgeLength)
                {
                    if (IsCancellationRequested())
                        return new ByteRaster();

                    // Save to filesystem
                    string sTileName = TileNamer.Encode(x, y, TileCommon.EdgeLength);
                    rasteriser.WriteAsAscii(
                        Path.Combine(tile.Common.DirectoryIntermediate, IRasterBuilder.Filename(sTileName, _sRasterFilenameSpecifier, tile.Common.Version)),
                        x, y, x + TileCommon.EdgeLength, y + TileCommon.EdgeLength);
                }
            }

            //rasteriser.WriteAsAscii(Path.Combine(tile.DirectoryIntermediate, s12km12kmMapTileName + "_full.asc"));

            return rasteriser.Crop((int)bounds.MinX, (int)bounds.MinY,
                (int)bounds.MinX + TileCommon.EdgeLength, (int)bounds.MinY + TileCommon.EdgeLength);
        }

        public void SetShpFilenames(string[] inputFilenames)
        {
            _sShpFilenames = inputFilenames;
        }

        public void SetRasterSpecifier(string sSpecifier)
        {
            _sRasterFilenameSpecifier = sSpecifier;
        }

        public void RemoveRasterizedClassesWithRasterValues()
        {
            _nlsClassesToRasterValues.Clear();
        }
    }
}
