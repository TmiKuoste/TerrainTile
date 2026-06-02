using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Reflection;
using NetTopologySuite.Geometries;
using LasUtility.Nls;
using System.Threading;
using System.Globalization;
using System.IO;
using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.Common.Loggers;
using Kuoste.TerrainTile.Tiles.BuilderServices;
using Kuoste.TerrainEngine.TileBuilders.DemDsm;
using Kuoste.TerrainEngine.TileBuilders.Rasters;
using Kuoste.TerrainEngine.TileBuilders.Buildings;
using Kuoste.TerrainEngine.TileBuilders.Trees;
using Kuoste.TerrainEngine.TileBuilders.WaterAreas;
using Kuoste.TerrainTile.Tools;

namespace Kuoste.TerrainTile.Tiles
{
    public class TileManager : MonoBehaviour
    {
        public GameObject TerrainTemplate;
        public GameObject WaterPlane;
        public Material BuildingRoof;
        public Material BuildingWall;

        public string RenderedArea;

        /// <summary>
        /// Folder where data from Nls is found
        /// </summary>
        public string DataDirectoryOriginal;

        /// <summary>
        ///  Folder for saving the rasterised / triangulated data
        /// </summary>
        public string DataDirectoryIntermediate;

        private Thread _dsmPointCloudThread;
        private Thread _rasterThread;
        private Thread _geometryThread;

        private readonly string _sVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        private readonly List<Tile> _terrainTilesInProcess = new();

        private Coordinate _origo;
        private Vector3 _heightmapScale;

        private CancellationTokenSource _cancellationTokenSource;

        private LogLevel _LogLevel = LogLevel.Debug;
        private bool _bLogConsole = false;
        private bool _bLogFile = true;
        private bool _bLogUnity = true;

        public int GetTilesInProcessCount()
        {
            return _terrainTilesInProcess.Count;
        }

        private void Awake()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Stopwatch sw = Stopwatch.StartNew();

            if (!Directory.Exists(DataDirectoryOriginal))
                Debug.Log($"Cannot find data from {nameof(DataDirectoryOriginal)}: " + DataDirectoryOriginal);

            if (!Directory.Exists(DataDirectoryIntermediate))
                Directory.CreateDirectory(DataDirectoryIntermediate);

            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _cancellationTokenSource.Token;

            CompositeLogger loggerDsm = new(_LogLevel, _bLogConsole, _bLogFile, Path.Combine(DataDirectoryIntermediate, "Dsm.log"));
            CompositeLogger loggerRaster = new(_LogLevel, _bLogConsole, _bLogFile, Path.Combine(DataDirectoryIntermediate, "Raster.log"));
            CompositeLogger loggerGeometry = new(_LogLevel, _bLogConsole, _bLogFile, Path.Combine(DataDirectoryIntermediate, "Geometry.log"));
            loggerDsm.AddLogger(new UnityLogger());
            loggerRaster.AddLogger(new UnityLogger());
            loggerGeometry.AddLogger(new UnityLogger());

            ITileBuilderService DsmPointCloudService = new TileDsmPointCloudService(
                    new DemDsmReader() { CancellationToken = token, Logger = loggerDsm },
                    new DemDsmCreator() { CancellationToken = token, Logger = loggerDsm },
                    token, loggerDsm);

            ITileBuilderService RasterService = new TileRasterService(
                new RasterReader() { CancellationToken = token, Logger = loggerRaster },
                new RasterCreator() { CancellationToken = token, Logger = loggerRaster },
                token, loggerRaster);

            ITileBuilderService GeometryService =
                new TileGeometryService(
                    new BuildingsReader() { CancellationToken = token, Logger = loggerGeometry },
                    new BuildingsCreator() { CancellationToken = token, Logger = loggerGeometry },
                    new TreeReader() { CancellationToken = token, Logger = loggerGeometry },
                    new SimpleTreeCreator() { CancellationToken = token, Logger = loggerGeometry },
                    new WaterAreasReader() { CancellationToken = token, Logger = loggerGeometry },
                    new WaterAreasCreator() { CancellationToken = token, Logger = loggerGeometry },
                    token, loggerGeometry);

            _dsmPointCloudThread = new(() => DsmPointCloudService.BuilderThread());
            _rasterThread = new(() => RasterService.BuilderThread());
            _geometryThread = new(() => GeometryService.BuilderThread());

            _dsmPointCloudThread.Start();
            _rasterThread.Start();
            _geometryThread.Start();

            TerrainData terrainData = TerrainTemplate.GetComponent<Terrain>().terrainData;
            _heightmapScale = terrainData.heightmapScale;
            TileCommon common = new(
                terrainData.alphamapResolution,
                Path.GetFullPath(DataDirectoryIntermediate), 
                Path.GetFullPath(DataDirectoryOriginal),
                _sVersion);

            if (string.IsNullOrEmpty(RenderedArea))
            {
                string[] lazFiles = Directory.GetFiles(DataDirectoryOriginal, "*.laz");

                Envelope boundsTotal = new();

                foreach (string file in lazFiles)
                {
                    string sTileName = Path.GetFileNameWithoutExtension(file);
                    TileNamer.Decode(sTileName, out Envelope boundsFile);
                    boundsTotal.ExpandToInclude(boundsFile);

                    AddTilesInBounds(DsmPointCloudService, RasterService, GeometryService, common, boundsFile);
                }

                _origo = new Coordinate(boundsTotal.Centre.X, boundsTotal.Centre.Y);
            }
            else
            {
                TileNamer.Decode(RenderedArea, out Envelope bounds);
                _origo = new Coordinate(bounds.Centre.X, bounds.Centre.Y);

                AddTilesInBounds(DsmPointCloudService, RasterService, GeometryService, common, bounds);
            }
        }

        private void AddTilesInBounds(ITileBuilderService DsmPointCloudService, ITileBuilderService RasterService, ITileBuilderService GeometryService, TileCommon common, Envelope bounds)
        {
            for (int x = (int)bounds.MinX; x < bounds.MaxX; x += TileCommon.EdgeLength)
            {
                for (int y = (int)bounds.MinY; y < bounds.MaxY; y += TileCommon.EdgeLength)
                {
                    string sTileName = TileNamer.Encode(x, y, TileCommon.EdgeLength);

                    Tile t = new()
                    {
                        Name = sTileName,
                        Common = common,
                    };

                    DsmPointCloudService.AddTile(t);
                    RasterService.AddTile(t);
                    GeometryService.AddTile(t);

                    _terrainTilesInProcess.Add(t);
                }
            }
        }

        void OnApplicationQuit()
        {
            // Send cancellation signal to threads for manual exit
            _cancellationTokenSource.Cancel();

            // Wait for threads to exit
            _dsmPointCloudThread.Join();
            _rasterThread.Join();
            _geometryThread.Join();
        }


        // Update is called once per frame
        private void Update()
        {
            for (int i = _terrainTilesInProcess.Count - 1; i >= 0; i--)
            {
                if (_terrainTilesInProcess[i].IsCompleted)
                {
                    Tile tile = _terrainTilesInProcess[i];
                    _terrainTilesInProcess.RemoveAt(i);

                    TileNamer.Decode(tile.Name, out Envelope bounds);
                    Vector3 pos = new((float)(bounds.MinX - _origo.X), 0, (float)(bounds.MinY - _origo.Y));
                    GameObject terrain = Instantiate(TerrainTemplate, pos, Quaternion.identity, transform);
                    terrain.name = tile.Name;

                    // Create and assign a deep copy of the terrain data
                    TerrainData newTerrainData = TerrainDataCloner.Clone(terrain.GetComponent<Terrain>().terrainData);
                    terrain.GetComponent<Terrain>().terrainData = newTerrainData;
                    terrain.GetComponent<TerrainCollider>().terrainData = newTerrainData;

                    // Add the tile updater component and assign the tile
                    TileUpdater tileUpdater = terrain.AddComponent<TileUpdater>();
                    tileUpdater.SetTile(tile);
                    tileUpdater.SetUnityVariables(_heightmapScale, WaterPlane, BuildingRoof, BuildingWall);
                    
                    if (_terrainTilesInProcess.Count == 0)
                    {
                        Debug.Log("All tiles instantiated");
                    }
                }
            }
        }
    }
}


