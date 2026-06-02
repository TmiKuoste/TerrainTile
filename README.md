# TerrainTile

Builds realistic 3D terrain tiles from [NLS Finland](https://www.maanmittauslaitos.fi/en)
point clouds (`.laz`) and geospatial data (shapefiles, rasters). The terrain engine is
platform-independent .NET and is consumed primarily as a Unity package — Unity is one output
profile, not the core.

[![Dynamic Landscapes in Unity Based on Real-World Data](https://img.youtube.com/vi/NTpnqi7m9Qw/0.jpg)](https://www.youtube-nocookie.com/embed/NTpnqi7m9Qw?si=X535YuvHWBivy9DT)

## Repository structure

| Path | What it is | Target |
| --- | --- | --- |
| [`TerrainEngine/`](TerrainEngine/) | Platform-independent core: `Tile`, builder interfaces, and concrete tile builders (`TerrainEngine.sln`). Published as the `TerrainEngine.TileBuilders` NuGet package. | `netstandard2.1` / C# 9 |
| [`fi.kuoste.terraintile/`](fi.kuoste.terraintile/) | Unity package (UPM) — runtime scripts, `TileManager`, samples, and vendored engine DLLs. See its [README](fi.kuoste.terraintile/README.md). | Unity |
| [`BuilderServices/`](BuilderServices/) | Dockerised console worker that builds tiles from jobs on an Azure Service Bus queue (`BuilderServices.sln`). | `net9.0` |

## How a tile is built

A `Tile` aggregates three content categories — each built independently and concurrently.
Each category has an `IXxxBuilder` interface with a `Reader` (loads from the intermediate
cache) and a `Creator` (builds from raw source data).

| Builder | Creator | Reader |
| --- | --- | --- |
| **DemDsm** | `DemDsmCreator` — reads a 3 km² LAZ point cloud, builds a Delaunay triangulation per 1 km² subtile with edge overlap to prevent gaps, rasterizes ground + vegetation points into a `VoxelGrid`, serializes to cache | `DemDsmReader` — deserializes the cached `VoxelGrid` |
| **Rasters** | `RasterCreator` — rasterizes NLS topographic DB shapefiles (building footprints, terrain type polygons) into a `ByteRaster` using NLS classification values | `RasterReader` — loads the cached ASCII raster |
| **Buildings** | `BuildingsCreator` — reads building footprints from shapefiles, samples point cloud heights (80th percentile), generates 3D meshes with separate wall and roof submeshes | `BuildingsReader` — deserializes cached building vertex/triangle data |
| **Trees** | `SimpleTreeCreator` — detects trees from the `VoxelGrid` by finding high-vegetation clusters (≥ 5 points, height 2–50 m) not adjacent to buildings or roads | `TreeReader` — deserializes cached tree point list |
| **WaterAreas** | `WaterAreasCreator` — clips terrain-type polygons from the topographic DB to the tile bounds, retaining water-area features | `WaterAreasReader` — deserializes cached water-area polygons |

## Building

```sh
# Core engine + tile builders
dotnet build TerrainEngine/TerrainEngine.sln

# Service worker (needs AZURE_SERVICE_BUS_CONNECTION_STRING and AZURE_SERVICE_BUS_SB_QUEUE_NAME)
dotnet build BuilderServices/BuilderServices.sln
dotnet run --project BuilderServices/BuilderServices.csproj

# Worker Docker image
docker build -t terraintile-builder -f BuilderServices/Dockerfile BuilderServices
```

The Unity package's play-mode tests run through the Unity Test Runner (not `dotnet test`).

## Contributing / AI agents

Coding guidance for both humans and AI tools lives in [`.github/`](.github/) and
[`AGENTS.md`](AGENTS.md) / [`CLAUDE.md`](CLAUDE.md). See
[`.github/instructions/repository-workflow.instructions.md`](.github/instructions/repository-workflow.instructions.md)
for branching and workflow conventions.

## License

Copyright © 2026 Vellu Sorvari / Kuoste. Free for personal, educational, and research use.
Commercial use requires a separate license — see [`LICENSE`](LICENSE) for full terms.
