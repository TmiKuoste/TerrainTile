# TerrainEngine.TileBuilders

Platform-independent .NET library for building 3D terrain tiles from NLS Finland point
clouds (`.laz`) and geospatial data (shapefiles, rasters). Consumed by the
[fi.kuoste.terraintile](https://github.com/Kuoste/TerrainTile) Unity package, but has no
Unity dependency.

## What it builds

A `Tile` aggregates three content categories — each built independently and concurrently:

| Category | Types |
| --- | --- |
| **DemDsm** | Elevation `VoxelGrid` (DEM/DSM) from LAS/LAZ point clouds |
| **Rasters** | `BuildingsRoads` and `TerrainType` (`IRaster`) |
| **Geometries** | `Trees`, `Buildings` (with wall/roof submeshes), `WaterAreas` |

A tile reports `IsCompleted` once all three categories are done.

## Builders

Each category has an `IXxxBuilder` interface with two implementations — a `Creator` that
builds from raw source data and a `Reader` that loads pre-built content from the intermediate
file cache. Both extend the shared `Builder` base, which carries a `CancellationToken` and
`ILogger`.

### DemDsm

| Class | Description |
| --- | --- |
| `DemDsmCreator` | Reads a 3 km² LAZ point cloud, filters ground and vegetation point classes, builds a Delaunay triangulation per 1 km² subtile with configurable edge overlap to prevent seam gaps, rasterizes to a `VoxelGrid` (DEM + DSM layers), then serializes each subtile to the intermediate cache. Processes up to a 3×3 grid of subtiles per LAZ file and deduplicates concurrent requests. |
| `DemDsmReader` | Deserializes a pre-built `VoxelGrid` for the requested tile from the intermediate cache. |

### Rasters

| Class | Description |
| --- | --- |
| `RasterCreator` | Rasterizes NLS topographic DB shapefiles (building footprints, terrain-type polygons) into a `ByteRaster` using caller-supplied NLS classification → raster-value mappings. Writes an ASCII raster to the cache. |
| `RasterReader` | Loads a pre-built `ByteRaster` from the ASCII file cache. Requires a raster specifier (e.g. `"BuildingsRoads"`) to locate the correct file. |

### Buildings

| Class | Description |
| --- | --- |
| `BuildingsCreator` | Reads building footprints from NLS topographic DB shapefiles, samples point cloud heights inside each footprint, derives building height from the 80th-percentile DSM value, and generates 3D mesh geometry with separate wall and roof submeshes. Caches the result as a text file. |
| `BuildingsReader` | Deserializes cached building geometry, reconstructing per-building vertex arrays, triangle index arrays, and the submesh separator between roof and wall triangles. |

### Trees

| Class | Description |
| --- | --- |
| `SimpleTreeCreator` | Detects individual trees from the `VoxelGrid` by locating high-vegetation voxels (height 2–50 m) with at least 5 high-vegetation neighbours within a 2-cell radius that are not adjacent to buildings or roads (3-cell exclusion). Caches absolute tree coordinates as text; returns tile-relative normalized positions. |
| `TreeReader` | Deserializes a cached list of tree `Point` positions (tile-relative X/Y + absolute Z). |

### WaterAreas

| Class | Description |
| --- | --- |
| `WaterAreasCreator` | Reads terrain-type polygon features from NLS topographic DB shapefiles, clips them to the 1 km² tile bounds, retains water-area features, and assigns each a surface height from the lowest DEM point on its boundary. Caches results as text (GeoJSON-style polygons). |
| `WaterAreasReader` | Deserializes cached water-area polygons into `NetTopologySuite` `Polygon` objects. |

## Usage

```csharp
var tile = new Tile { Name = "L4133B1", Common = tileCommon };

// First run: build from source data
var demBuilder = new DemDsmCreator { Logger = myLogger, CancellationToken = cts.Token };
tile.DemDsm = demBuilder.Build(tile);

// Subsequent runs: load from cache
var demReader = new DemDsmReader { Logger = myLogger, CancellationToken = cts.Token };
tile.DemDsm = demReader.Build(tile);
```

## Dependencies

- [LasUtility](https://github.com/Kuoste/LasZipNetStandard) — LAS/LAZ point cloud reading
- [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) — geometry, [BSD-3-Clause](https://licenses.nuget.org/BSD-3-Clause)

## License

Copyright © 2026 Vellu Sorvari / Kuoste. Free for personal, educational, and research use.
Commercial use requires a separate license — contact via
[LinkedIn](https://www.linkedin.com/in/vellusorvari/).
