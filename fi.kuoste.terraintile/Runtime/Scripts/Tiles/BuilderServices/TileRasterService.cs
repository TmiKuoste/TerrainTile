using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Loggers;
using Kuoste.TerrainEngine.Common.Tiles;
using LasUtility.Nls;
using LasUtility.Shapefile;
using NetTopologySuite.Geometries;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Kuoste.TerrainTile.Tiles.BuilderServices
{
    public class TileRasterService : TileService, ITileBuilderService
    {
        private readonly IRasterBuilder _reader, _creator;

        private readonly Dictionary<int, byte> _buildingRoadClassesToRasterValues = new();
        private readonly Dictionary<int, byte> _terrainTypeClassesToRasterValues = new();

        public TileRasterService(IRasterBuilder reader, IRasterBuilder creator, 
            CancellationToken token, CompositeLogger logger)
        {
            _reader = reader;
            _creator = creator;

            // Combine raster values for buildings and roads

            foreach (var mapper in TopographicDb.RoadLineClassesToRasterValues)
                _buildingRoadClassesToRasterValues[mapper.Key] = mapper.Value;

            foreach (var mapper in TopographicDb.BuildingPolygonClassesToRasterValues)
                _buildingRoadClassesToRasterValues[mapper.Key] = mapper.Value;


            // Combine raster values for terrrain features

            foreach (var mapper in TopographicDb.WaterPolygonClassesToRasterValues)
                _terrainTypeClassesToRasterValues[mapper.Key] = mapper.Value;

            foreach (var mapper in TopographicDb.SwampPolygonClassesToRasterValues)
                _terrainTypeClassesToRasterValues[mapper.Key] = mapper.Value;

            foreach (var mapper in TopographicDb.RockPolygonClassesToRasterValues)
                _terrainTypeClassesToRasterValues[mapper.Key] = mapper.Value;

            foreach (var mapper in TopographicDb.SandPolygonClassesToRasterValues)
                _terrainTypeClassesToRasterValues[mapper.Key] = mapper.Value;

            foreach (var mapper in TopographicDb.FieldPolygonClassesToRasterValues)
                _terrainTypeClassesToRasterValues[mapper.Key] = mapper.Value;

            foreach (var mapper in TopographicDb.RockLineClassesToRasterValues)
                _terrainTypeClassesToRasterValues[mapper.Key] = mapper.Value;

            _token = token;
            _logger = logger;
        }

        public void BuilderThread()
        {
            while (true)
            {
                if (_token != null && _token.IsCancellationRequested)
                    return;

                if (_tileQueue.Count > 0 && _tileQueue.TryDequeue(out Tile tile))
                {
                    Stopwatch swRead = new();
                    Stopwatch swCreate = new();

                    TileNamer.Decode(tile.Name, out Envelope bounds);
                    string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);


                    // Process terrain type raster

                    string sFilename = IRasterBuilder.Filename(tile.Name, IRasterBuilder.SpecifierTerrainType, tile.Common.Version);
                    string sFullFilename = Path.Combine(tile.Common.DirectoryIntermediate, sFilename);

                    if (File.Exists(sFullFilename))
                    {
                        // Load raster from filesystem
                        swRead.Start();
                        _reader.SetRasterSpecifier(IRasterBuilder.SpecifierTerrainType);
                        tile.TerrainType = _reader.Build(tile);
                        swRead.Stop();
                    }
                    else
                    {
                        // Create raster from shapefiles
                        swCreate.Start();
                        _creator.SetRasterSpecifier(IRasterBuilder.SpecifierTerrainType);
                        _creator.SetRasterizedClassesWithRasterValues(_terrainTypeClassesToRasterValues);
                        _creator.SetShpFilenames(new string[] { TopographicDb.sPrefixForTerrainType + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp" });
                        tile.TerrainType = _creator.Build(tile);
                        swCreate.Stop();
                    }


                    // Process buildings & roads raster

                    sFilename = IRasterBuilder.Filename(tile.Name, IRasterBuilder.SpecifierBuildingsRoads, tile.Common.Version);
                    sFullFilename = Path.Combine(tile.Common.DirectoryIntermediate, sFilename);

                    if (File.Exists(sFullFilename))
                    {
                        // Load raster from filesystem
                        swRead.Start();
                        _reader.SetRasterSpecifier(IRasterBuilder.SpecifierBuildingsRoads);
                        tile.BuildingsRoads = _reader.Build(tile);
                        swRead.Stop();
                    }
                    else
                    {
                        // Create raster from shapefiles
                        swCreate.Start();
                        _creator.SetRasterSpecifier(IRasterBuilder.SpecifierBuildingsRoads);
                        _creator.SetRasterizedClassesWithRasterValues(_buildingRoadClassesToRasterValues);
                        _creator.SetShpFilenames(new string[]
                        {
                            TopographicDb.sPrefixForRoads + s12km12kmMapTileName + TopographicDb.sPostfixForLine + ".shp",
                            TopographicDb.sPrefixForBuildings + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp"
                        });
                        tile.BuildingsRoads = _creator.Build(tile);
                        swCreate.Stop();
                    }

                    Interlocked.Increment(ref tile.CompletedCount);

                    if (swRead.Elapsed.TotalMilliseconds > 0)
                        _logger.LogInfo($"Tile {tile.Name} rasters read in {swRead.Elapsed.TotalSeconds} s.");
                    if (swCreate.Elapsed.TotalMilliseconds > 0)
                        _logger.LogInfo($"Tile {s12km12kmMapTileName} rasters created in {swCreate.Elapsed.TotalSeconds} s.");

                    Thread.Sleep(10);
                }
                else
                {
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
