# fi.kuoste.terraintile

Welcome to the `fi.kuoste.terraintile` repository! This Unity package is designed for creating realistic landscapes from NLS Finland point clouds and other geospatial data.

Click the image below to see the package in action!

[![Dynamic Landscapes in Unity Based on Real-World Data](https://img.youtube.com/vi/NTpnqi7m9Qw/0.jpg)](https://www.youtube.com/watch?v=NTpnqi7m9Qw)


## Features

- **Terrain Prefab**: Utilize the included terrain prefab to kickstart your landscape creation.
- **GIS Data Integration**: Seamlessly import GIS data to shape your terrain to real-world topography.
- **Point Cloud Processing**: Convert point clouds into detailed terrain meshes.

## How terrain is generated

The package delegates tile content building to the `TerrainEngine.TileBuilders` library.
Each tile category is built concurrently:

| Category | Creator | What it does |
| --- | --- | --- |
| **DemDsm** | `DemDsmCreator` | Reads LAZ point clouds, builds a Delaunay triangulation per 1 km² subtile with edge overlap to prevent seams, rasterizes to a `VoxelGrid` (DEM + DSM) |
| **Rasters** | `RasterCreator` | Rasterizes NLS topographic DB shapefiles (buildings/roads, terrain type) into `ByteRaster` layers |
| **Buildings** | `BuildingsCreator` | Reads building footprints from shapefiles, derives height from point cloud (80th-percentile DSM), generates 3D meshes with separate wall and roof submeshes |
| **Trees** | `SimpleTreeCreator` | Detects trees from high-vegetation voxel clusters (height 2–50 m, ≥ 5 neighbours), excluding cells near buildings or roads |
| **WaterAreas** | `WaterAreasCreator` | Clips water-area polygons from terrain-type shapefiles to the tile bounds |

On subsequent runs each category is loaded from an intermediate file cache instead of being
rebuilt from source data.

## Getting Started

To get started with `fi.kuoste.terraintile`, clone this repository and import the package into your Unity project. Follow the instructions in the `Samples` folder to learn how to integrate point clouds and GIS data into your terrain.

## License

Copyright © 2026 Vellu Sorvari / Kuoste. All rights reserved.

**You may**, free of charge:
- Use, study, and modify this software for personal, educational, or research purposes.
- Share unmodified or modified copies non-commercially, provided you credit the original author.

**You may not**, without a separate written commercial license:
- Use this software, in whole or in part, in any product or service that is sold, licensed, or
  otherwise provided for a fee or other commercial benefit.
- Use this software as part of internal tooling at a for-profit company beyond personal experimentation.

To obtain a commercial license, contact Vellu Sorvari on
[LinkedIn](https://www.linkedin.com/in/vellusorvari/).

**No warranty.** This software is provided "as is", without warranty of any kind, express or
implied, including but not limited to the warranties of merchantability, fitness for a
particular purpose, and non-infringement. In no event shall the author be liable for any
claim, damages, or other liability arising from the use of this software.

## 3rd party libraries
 - [LASZip](https://github.com/LASzip/LASzip), [Apache-2.0 License](http://www.apache.org/licenses/LICENSE-2.0)
 - [LasZipNetStandard](https://github.com/Kuoste/LasZipNetStandard), [Apache-2.0 License](http://www.apache.org/licenses/LICENSE-2.0)
 - [MessagePack](https://github.com/MessagePack-CSharp/MessagePack-CSharp), [MIT License](https://en.wikipedia.org/wiki/MIT_license)
 - [MIConvexHull](https://github.com/DesignEngrLab/MIConvexHull), [MIT License](https://en.wikipedia.org/wiki/MIT_license)
 - [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite), [BSD-3-Clause](https://licenses.nuget.org/BSD-3-Clause)
