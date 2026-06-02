# TerrainTile — AI Coding Instructions

Shared guidance for Claude Code, GitHub Copilot, and other agents. This file is always in
context; layer-specific rules live in `.github/instructions/*.instructions.md` and are
applied by path (Copilot via `applyTo`, Claude via imports in `CLAUDE.md`).

## What this project is

TerrainTile builds realistic 3D terrain tiles from NLS Finland point clouds (`.laz`) and
geospatial data (shapefiles, rasters). The terrain engine is consumed primarily as a Unity
package, but Unity is just one output profile — keep the core builders Unity-agnostic.

## Repository layout

- `TerrainEngine/` — the platform-independent .NET core (`TerrainEngine.sln`).
  - `Common/` (`netstandard2.1`) — `Tile`, `TileCommon`, and the `IXxxBuilder` interfaces.
  - `TileBuilders/` (`netstandard2.1`) — concrete builders. Packaged as the
    `TerrainEngine.TileBuilders` NuGet package.
  - → detailed rules: `.github/instructions/dotnet-core.instructions.md`
- `fi.kuoste.terraintile/` — the Unity package (UPM). `Runtime/Scripts` holds the
  Unity-side services and `TileManager`; pre-built engine DLLs are vendored in `Runtime/dll/`.
  - → detailed rules: `.github/instructions/unity.instructions.md`
- `BuilderServices/` (`net9.0`, `BuilderServices.sln`) — a Dockerised console worker that
  reads build jobs from an Azure Service Bus queue.
  - → detailed rules: `.github/instructions/services.instructions.md`

## Core architecture

A `Tile` (`TerrainEngine/Common/Tiles/Tile.cs`) aggregates three content categories, and is
only `IsCompleted` once all three are built (`CompletedRequired == 3`):

1. **DemDsm** — the elevation `VoxelGrid` (DEM/DSM).
2. **Rasters** — `BuildingsRoads` and `TerrainType` (`IRaster`).
3. **Geometries** — `Trees`, `Buildings`, and `WaterAreas`.

Each content type has a builder interface in `Common/Interfaces/` (e.g. `IDemDsmBuilder`)
with two implementations under `TileBuilders/`: an **`XxxReader`** that deserializes
already-built content from the intermediate cache, and an **`XxxCreator`** that produces it
from source data. Both extend the shared `Builder` base, which carries the `CancellationToken`
and `ILogger`.

## How AI agents should work in this repo

See `.github/instructions/repository-workflow.instructions.md` for branching, commit style,
layering rules, the Reader/Creator contract, and verification expectations.

## Third-party

LasUtility/LASZip (point clouds), NetTopologySuite (geometry), DelaunatorSharp
(triangulation), MessagePack (serialization), MIConvexHull. Licensing terms are in
`fi.kuoste.terraintile/README.md`.
