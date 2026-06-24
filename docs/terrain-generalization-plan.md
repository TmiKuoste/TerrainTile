# Terrain triangulation generalization — draft plan

> Status: **draft, iterating**. Started 2026-06-15 on branch `39-add-terrainengine-tests` (planning only; no code yet).
> This is a design/planning doc, not a commitment. See also memory `project_terrain_generalization`.

## Goal

Generalize the DEM/DSM triangulation so it supports:

1. Triangulating whole **3×3 km** source tiles in one block, instead of always splitting them into nine 1×1 km submeshes first.
2. Still handling **1×1 km** source tiles — the dense 5 pts/m² (`5p`) NLS data is only delivered in 1 km tiles.
3. **Parallel** triangulation when the machine has enough RAM.
4. Solving the currently-unsolved **seams between adjacent source tiles**.
5. A path toward **other countries' data** (non-NLS tiling/naming).

> **Scope note:** this began as triangulation, but the discussion has grown into the product around it —
> captured in the later sections: on-demand acquisition for **any location** (generalizable beyond
> Finland to the other Nordics), cloud/browser deployment, load-time performance, a **lightweight
> zero-backend browser tier**, **progressive DEM quality tiers**, and **enhanced point-cloud analysis**
> (trees, roofs).

## Current architecture & root cause

Everything runs through `DemDsmCreator.Build` (`TerrainEngine/TileBuilders/DemDsm/DemDsmCreator.cs`):
for any requested 1 km tile it opens the enclosing **3 km** `.laz`, splits it into **nine 1 km submeshes**
(each its own `SurfaceTriangulation` + `VoxelGrid`), duplicates edge points into neighbour submeshes to
avoid internal gaps, triangulates each, and writes nine `.voxelgrid` files. The cross-file seam is the
explicit TODO at `DemDsmCreator.cs:134` — points near the 3 km edge that belong to a neighbouring `.laz`
are dropped.

**Root cause of the limitations:** `TileCommon.EdgeLength = 1000` (`TerrainEngine/Common/Tiles/TileCommon.cs`)
is doing **three jobs at once**:

| Concept | Meaning | Today | Should be |
|---|---|---|---|
| **Source tile** | the `.laz` file extent | hardcoded 3 km | from data: 3 km (0.5p) or 1 km (5p) |
| **Output tile** | one Unity `Terrain` / one cached `.voxelgrid` | 1 km | configurable; Unity wants `2^n` m |
| **Triangulation block** | one `SurfaceTriangulation` work unit | 1 km (the 9-way split) | a configurable knob (1–3 km) |

Decoupling these three is the foundation for all five goals.

## Design decisions (settled in discussion)

- **`ITileScheme` abstraction** in `Common/Interfaces` — `Encode`/`Decode`/`Neighbors`/sub-tiles/sizes.
  `NlsTileScheme` wraps the existing `LasUtility.Nls.TileNamer`; `GridTileScheme` is a generic
  origin+size grid for other countries **and** for output tiling. Source and output use *different*
  scheme instances. `Neighbors()` is also exactly the primitive the seam halo needs.
- **Configurable block size (1–3 km), driven by RAM not just density.** A low-mem cloud worker must be
  able to fall back to **1 km blocks even for sparse 0.5p** data, so 1 km stays valid regardless of
  density; block size is chosen from a RAM budget, not hardcoded by density. (1 km = lower peak, more
  parallel; 3 km = fewer seams.)
- **Parallelism = a shared RAM-budget pool.** Each block estimates its peak (≈ point-count × bytes;
  ~2.5 GB for a 3 km block) and **reserves** from a shared budget before starting; runs when the budget
  allows, else waits. The budget is **configurable via env var** (e.g. `TERRAIN_RAM_BUDGET_MB`),
  defaulting to a fraction of detected available RAM. Same model whether workers are threads (an
  in-process semaphore over the pool) or separate Azure Functions/containers (the "pool" becomes a
  concurrency cap / queue depth tuned per instance's memory). That's the answer to "shared RAM pool
  between threads or functions" — one budget, two enforcement mechanisms.
- **Configurable, arbitrary output size** — `2^n` m (e.g. 1024 → 1025 heightmap, no resample) is a
  *Unity profile*, not a core constraint. Fixes the 1000→1025 heightmap aliasing. Output gets its own
  naming scheme, decoupled from NLS source tiling.
- **Seams via write-behind halo bands** — on ingest, extract thin ground-point bands per neighbour
  edge/corner (via `ITileScheme.Neighbors()`), persist as MessagePack sidecars, consume when the
  neighbour block builds. License-clean and robust to file ordering.
- **COPC set aside** — not a drop-in win (see findings). A custom chunk-interval index is an *optional*
  general capability, not required for seams.

## Empirical findings (probe over `D:\data` Helsinki L4133, 0.5 pts/m²)

**Point ordering** — for a 50 m halo edge-strip, fraction of 50k-point chunks that must be read:

| File | W | E | S | N |
|---|---|---|---|---|
| `L4133B1.laz` (regular) | 6% | 8% | 20% | 15% |
| `L4133D4.laz` (regular) | 6% | 7% | 11% | 13% |
| `L4133D4.copc.laz` (COPC) | 19% | 23% | 26% | 27% (idx-span 100%) |

- Native NLS `.laz` is already **spatially blocked** → an interval-based chunk index would prune a halo
  read to ~10–20%. COPC is **worse** for chunk pruning (octree level-interleaving); it only helps with
  true octree-node queries (a streaming/LOD use-case), so set it aside here.
- For the halo use-case, push (write-behind) ≈ pull-with-index in total IO (~2× reads) → **push wins on
  simplicity**.

**3 km single-block triangulation spike** (`L4133B1`, 3.93M ground points):

```
load + addpoint    : 4.9 s
tri.Create()       : 11.4 s
peak working set   : ~2.5 GB
TOTAL              : 16.6 s (single-threaded)
```

- A 3 km block is feasible but **~2.5 GB peak per block** → parallelism must be RAM-gated
  (4 parallel ≈ 10 GB, 8 ≈ 20 GB).
- **Earlier risk RESOLVED (2026-06-17):** the `SetMissingHeightsFromTriangulation` throw at 3 km was a
  *probe bug* — mismatched **float** bounds (`ExpandBy(2)`) vs. the **int** extent args. With
  **integer-aligned** grid/triangulation bounds (`Floor`/`Ceil` of the reader extent) it works at full
  3 km **with no LasUtility change** (verified in the fixed LazProbe). No downstream 1 km assumption
  blocks large blocks.

## Phased implementation plan

Each phase builds & tests green on its own; biggest blast radius last.

- **Phase 0 — (no longer needed).** The suspected LasUtility extent fix is unnecessary:
  `SetMissingHeightsFromTriangulation` works at 3 km when the grid/triangulation use **integer-aligned
  bounds** (`Floor`/`Ceil`); the earlier throw was a probe bug. Just use that call pattern in
  `DemDsmCreator` — no LasUtility change, no re-vendor.
- **Phase 1 — `ITileScheme`, zero behaviour change.** New interface; `NlsTileScheme` wraps `TileNamer`;
  route `DemDsmCreator`, `TileManager`, BuilderServices through it, still NLS 1/3 km. Pure refactor,
  existing tests green. *Propose interface shape for review before wiring (repo "ask before restructure"
  rule).*
- **Phase 2 — Decouple the three sizes in config.** Retire the `EdgeLength` global const; carry output
  size + block size on `TileCommon`/`Tile`. Defaults reproduce today's behaviour.
- **Phase 3 — Block-size knob via bbox distribution (KEEP the submesh capability).** Replace the
  ~116-line directional overlap **juggling** with a single **bounding-box distribution**: build *N*
  triangulation blocks where `N = (SourceEdgeLength / BlockEdgeLength)²`, push each point into every
  grid/block whose extent contains it, then triangulate, rasterise and free each block in turn. One knob,
  two regimes: `BlockEdgeLength == OutputEdgeLength` reproduces the **1 km per-submesh** path (the
  **default**, the cheap **low-mem** option for inexpensive cloud workers, ~280 MB/block);
  `BlockEdgeLength == SourceEdgeLength` triangulates the **whole source in one block** (no internal seams,
  ~2.5 GB for a 3 km block). Block size is thus a **memory/cost knob**, not a one-off rewrite. Default
  behaviour unchanged (verified: whole-block DEM agrees with submesh DEM within 0.5 m on >95% of the
  interior tile); intra-source seams vanish on the whole-block path; cross-source seams still TODO until
  Phase 5.
- **Phase 4 — RAM-gated parallelism.** A **shared RAM-budget pool**, configurable via env var
  (e.g. `TERRAIN_RAM_BUDGET_MB`, default a fraction of detected RAM); each block reserves its estimated
  peak (~2.5 GB/3 km, scaled by point count) before starting. **1 km blocks stay available even for
  sparse data** so low-mem workers still run. In-process = a semaphore over the pool; across Azure
  Functions/containers = a concurrency cap tuned per instance. Wire into the worker and the Unity thread.
- **Phase 5 — Cross-source halo (write-behind bands).** Closes the seam TODO. Extract/persist/consume
  neighbour halo bands via `ITileScheme.Neighbors()`.
- **Phase 6 — Output-grid generalization (largest blast radius, last).** `GridTileScheme` (arbitrary /
  `2^n`), new output-cache naming, re-tile rasters + geometries (`RasterCreator`, buildings/trees/water)
  onto the output scheme. Delivers the heightmap no-resample fix and non-Unity/arbitrary sizes.
- **Phase 7 — (optional, later) custom spatial index.** License-clean chunk-interval index + `seek`
  primitive in laszipnetstandard for on-demand spatial reads. Not needed for seams.

**Cross-cutting:** every LasUtility change must be re-vendored to `Runtime/dll/`; new cache naming/version
invalidates the existing `D:\data\intermed` artifacts (expected).

**Suggested first PR:** Phase 1 (the `ITileScheme` abstraction everything hangs off). Phase 0 is moot.

## Architecture & deployment (cloud / browser)

The engine **core is already platform-neutral and cloud-ready** — the builders, Reader/Creator split,
content types and `ILogger` never touch Unity. The layering paid off. Two boundaries are misplaced:

1. **Orchestration is trapped in Unity but isn't Unity.** The queues, `BuilderThread()` loops and the
   "if cached → Reader else → Creator" dispatch live in
   `fi.kuoste.terraintile/Runtime/Scripts/Tiles/BuilderServices/`, yet every file there imports
   `UnityEngine` **without using a single Unity type** (verified). That's why
   `BuilderServices/Program.cs` is a stub that builds nothing — the logic it needs is locked in the
   Unity package. **Change:** extract orchestration into a platform-neutral `netstandard2.1` project
   in `TerrainEngine/`; Unity keeps only `TileManager`, `UnityLogger`, `TileUpdater`,
   `TerrainDataCloner`. Mostly a move, low risk, unlocks everything else.
2. **IO is hardcoded to the local filesystem** (`File.Exists`, `Path.Combine`,
   `VoxelGrid.Serialize(path)`). **Change:** an `IStorage` abstraction (`Exists`/`OpenRead`/`OpenWrite`
   by key). **Local-first** by intent — local FS on desktop; browser-side cache (IndexedDB / HTTP) for
   WebGL; blob only if/when a server build tier wants it. Same Creator code, pluggable backend. Needed
   for the intermediate cache + halo sidecars regardless.
3. **Job state is welded to the data model.** `Tile` carries both content *and* orchestration state
   (`CompletedCount` via `Interlocked`, `IsCompleted`). Three threads mutating one shared `Tile` is a
   fine *in-process* design and a dead end across workers. **Change:** keep `Tile` as pure data; move
   "all 3 parts done?" to external state (message ack / job record / "artifacts exist"). Same for the
   in-process dedup `DemDsmCreator._3kmDemDsmDone` → becomes a job-granularity rule (one source tile =
   one job) in a dispatcher.
4. **No job contract / planner.** Define a job schema (source extent, block size, output scheme/size,
   version, output location) and a planner that turns "build area X at config Y" into idempotent jobs
   (today implicit in `TileManager.AddTilesInBounds`). And make **cache keys config-aware** — once
   block/output sizes and scheme vary, `name + version` (`IDemDsmBuilder.Filename`) silently collides.

**Deployment targets:**

| Target | Build (Creators) | Consume (Readers) | Cache |
|---|---|---|---|
| Desktop (today) | in-process | in-process | local FS |
| Cloud build + **browser** consume | server-side or pre-baked on desktop | Unity **WebGL** | lean artifacts served + browser-cached |

**WebGL makes the build/consume split mandatory, not optional:** Unity WebGL is single-threaded and
can't load the native LASzip binding, so the Creators (point cloud → triangulation) **cannot run in a
browser**. A browser client is inherently **consume-only**, reading lean pre-built artifacts.

## Performance: intermediate load time (a current pain point)

**Finding (verified in `TileUpdater`):** the Unity consume path uses **only** `DemDsm.Dem[x,y]` (the
1000×1000 heightmap) — it never reads the per-cell points. The point accessors (`GetPoints`,
`GetHighestPointInClassRange`) are used **only by the build-time Creators** (buildings/trees). But the
`.voxelgrid` intermediate stores the **full point cloud** (millions of `BinPoint`s — the ~2 GB-heap
seen in the 3 km spike), so every tile deserializes millions of points just to read a ~4 MB float array.
That is the load-time cost, and the reason a browser couldn't stomach the current artifacts.

**Fix (same change serves desktop perf *and* browser):** split the artifact —
- **Lean render artifact**: just the DEM heightmap (the rasters and geometry outputs are already
  compact). Compact/quantized (raw `float` blit, or 16-bit like Unity's heightmap, optionally
  deflate → ~1–4 MB/tile). The consume `Reader` loads only this.
- **Heavy build intermediate**: **don't persist the voxel-grid-with-points at all.** It's a derived,
  single-resolution *in-memory index*, not data — and you're about to have several (grid, kd-tree,
  octree). Persisting one is exactly what makes loads slow. Separate **points** (source of truth) from
  **index** (rebuilt in RAM on demand). Re-reading the `.laz` + re-binning was ~5 s in the spike —
  plausibly *faster* than deserializing the bloated `.voxelgrid`, so persisting it may save nothing even
  for re-analysis. Decision rule: persist when `recompute × access_frequency ≫ storage + load`.

Confirm the bottleneck with a quick profile (IO vs. deserialization/allocations), but the split holds
regardless and is the single highest-impact perf change. It's also a prerequisite for the browser
target (ship ~MB, not tens of MB, per tile).

## Architecture & performance track (complementary to the triangulation phases)

- **A1 — `IStorage` abstraction (local-first).** Unblocks browser/cloud and the cache/halo work.
- **A2 — Extract orchestration out of Unity** into shared `netstandard2.1`.
- **A3 — Persist only the lean DEM render artifact; stop persisting the voxelgrid.** Rebuild
  grid/kd-tree/octree in memory from source on demand. Fixes load-time *today*; enables browser.
- **A4 — Separate job-state from `Tile`; job schema + dispatcher; config-aware cache keys.**

Sequencing: **A3 and A1 are worth doing now** (A3 fixes the load pain immediately, A1 is needed for the
generalization cache anyway). A2/A4 are the cloud/browser enablers and slot in after `ITileScheme`
(Phase 1) and the size knobs (Phase 2), which define the job schema.

## Data sources & acquisition (on-demand, any place in Finland)

Target: a browser (WebGL) client where an Azure backend builds any location in Finland on demand.
The one new layer in front of everything else is **data acquisition**.

**Single source for now: the official NLS (Maanmittauslaitos) open-data API.** `kapsi`
(`kartat.kapsi.fi`) mirrors the same data (LAZ, MTK as shapefile *and* GeoPackage, DEM, 3D buildings,
orthophotos; HTTP/FTP/rsync) — but its **laser files are organized by acquisition campaign, not by
tile name**:
```
laser/automaattinen/{year}/{YYYYMMDD}_{provider}_{place}_{season}/Harvennettu/L4133B1.laz
```
so there is **no `tile → URL` mapping** — you'd have to crawl/index it to answer "what covers this
point?". The NLS API answers a **coordinate/bbox query → available tiles + download URLs** directly
(no downloading a file to discover coverage), so it does both *resolve* and *fetch*. **kapsi is parked**
as a possible future bulk mirror (rsync) once we maintain our own tile index; it's volunteer-run, so
not for a production backend anyway.

**Acquisition flow:** location → NLS API (covering tiles **+ halo neighbours**) → download `.laz`
(1/3 km) + MTK (12 km) → cache via `IStorage` → build → lean artifacts → stream to browser per tile.

**`ISourceDataProvider` abstraction** (platform-neutral, mirrors `IStorage` but for *source* data):
`LocalFolder` (today/dev) and `NlsApi` (production). Note LAZ and MTK have different granularity, and
the halo needs neighbour tiles too.

**Generalize beyond Finland (other Nordics).** `ISourceDataProvider` + `ITileScheme` together make
acquisition country-pluggable: NLS is the first provider; Sweden/Norway/Denmark publish similar open
LiDAR + topographic data, so each becomes another provider + tile scheme + class/theme mapping. Keep the
NLS specifics (process IDs, `PointCloud05p` classes, `TopographicDb` themes) **behind the provider**, not
in the builders.

**Licensing:** NLS open data is CC BY 4.0 → display "© Maanmittauslaitos" in the app; free API key
required.

**NLS API surface (concrete, verified 2026-06-17):**
- **File download service** (async jobs) — base
  `https://avoin-paikkatieto.maanmittauslaitos.fi/tiedostopalvelu/ogcproc/v1/`, auth `?api-key=KEY`.
  POST `/processes/{id}/execution` with JSON inputs → returns jobID + status URL → poll → download links.
  - Laser 0.5p: `laserkeilausaineisto_05_karttalehti` — inputs `mapSheetInput` (tile name),
    `fileFormatInput:"LAZ"`, `dataSetInput:"Uusin"`. **By map sheet only — no bbox.**
  - MTK: `maastotietokanta_bbox` / `_karttalehti` / `_kunta` / `_polygon` — `themeInput`
    (`rakennukset`, `maasto`, `hydrografia`, `tieliikenne`…), `fileFormatInput` **GPKG | GML | SHAPE**.
  - 3D buildings: `3d-rakennukset_karttalehti` — `fileFormatInput:"CityGML"`, `levelOfDetailInput:"LOD2"`,
    by map sheet.
  - 2 m DEM: `korkeusmalli_2m_bbox` → TIFF (fallback tier). All in EPSG:3067.
- **MTK query service** (sync) — `https://avoin-paikkatieto.maanmittauslaitos.fi/maastotiedot/features/v1/`
  (OGC API Features): `bbox=…&bbox-crs=…/3067` → **GeoJSON**; collections `rakennus`, `tieviiva`, … —
  read straight into NTS, an alternative to downloading SHAPE bundles.

**Confirmed working recipe (live-tested 2026-06-17):**
- POST `…/processes/{id}/execution?api-key=KEY`, header `Content-Type: application/json`. The body
  **must include a top-level `"id"` matching the processId** *and* `"inputs"` — non-standard OGC, and the
  missing `"id"` is exactly what returns a bare `HTTP 400`. No `Prefer` header / no Basic auth needed.
  e.g. `{"id":"laserkeilausaineisto_05_karttalehti","inputs":{"mapSheetInput":["L4133B1"],"fileFormatInput":"LAZ","dataSetInput":"Uusin"}}`
- Response → `jobID` + `status:"accepted"`. Poll `…/jobs/{jobID}?api-key=KEY`
  (`accepted`→`running` w/ `progress` %→`successful`). Then GET `…/jobs/{jobID}/results/?api-key=KEY` →
  `results[].path` is a direct download URL (`…/tiedostopalvelu/dl/v1/{jobID}/<file>`) plus a `zipPath`;
  append `?api-key=` to the download URL.
- **Measured:** one laser tile (`L4133B1`) job = **~23 s** accepted→successful. Download throughput not
  benchmarked (flaky test network). Service is behind an **F5 gateway**; `mapSheetInput` takes up to
  **100 sheets/job** (batch an area + halo neighbours in one job).
- **All four data types confirmed live (good connection, 2026-06-17):**

  | data | process | selector | format | job | download |
  |---|---|---|---|---|---|
  | point cloud | `laserkeilausaineisto_05_karttalehti` | 3 km tile (1:5000), ≤100/job | LAZ | ~21 s | 28.9 MB @ ~7 MB/s (4.2 s) |
  | DEM 2 m | `korkeusmalli_2m_bbox` | bbox | **TIFF only** | ~5 s | 250 KB / 500 m box |
  | topographic DB | `maastotietokanta_bbox` | bbox + `themeInput` | **GPKG only** | ~25 s | ~5 MB |
  | topographic DB | `maastotietokanta_karttalehti` | 12 km sheet | GML / **ESRI shapefile** | — | — |
  | 3D buildings | `3d-rakennukset_karttalehti` | **6 km sheet (1:10000)** | CityGML `.zip` / LOD2 | ~3–9 s | 3–10 MB |

- **Sheet levels & gotchas:** the **3D-building sheet = laser tile minus its last digit** (`L4133B1` →
  `L4133B`, the 6 km/1:10000 parent). **Helsinki is fully covered** (`L4133A/B/C/D` all return data) —
  an earlier "data not found" was a wrong sheet, not missing coverage. MTK **bbox is GPKG-only**;
  SHAPE/GML only via the *map-sheet* (12 km) process. Cold-start per laser tile ≈ **~25 s** (21 s job +
  4 s download) at ~7 MB/s.

**Two consequences that simplify things:**
- **Laser resolution is local, not an API call.** The service downloads laser **by map-sheet name**, so
  compute the covering tiles (+ halo neighbours) from the coordinate via `ITileScheme`/`TileNamer`, then
  download each by name. This removes the kapsi "campaign" problem entirely.
- **MTK can be current SHAPE or GeoJSON** — the file service serves current MTK as SHAPE (no stale 2023
  snapshot, no forced GeoPackage reader), and the Features API serves GeoJSON by bbox. NLS 3D buildings
  are **CityGML LOD2**, so the fast tier already gives roof shapes — D3 just needs a CityGML importer.

## Buildings: fast (NLS 3D) vs high-detail (LiDAR) — two tiers

NLS publishes ready **3D building models** (`3D-rakennukset`). Use them as a **fast tier** (broad
coverage / instant results), while keeping **LiDAR-derived roofs as the high-detail default** — that's
the differentiator and works anywhere there's a point cloud. Footprints still come from MTK; the
roof/height comes from either:
- **NLS 3D model** (fast tier) — import directly; **CityGML LOD2** (confirmed) → needs a CityGML importer.
- **LiDAR roof reconstruction** (quality tier) — RANSAC / region-growing roof planes from points (uses
  the kd-tree from the point-cloud-analysis notes), with today's percentile-flat `BuildingsCreator` as
  the LOD1 fallback for sparse 0.5p / small buildings.

Architecturally these are alternative `IBuildingsBuilder` sources behind a per-tile/zoom selection
(fast vs. detailed), all writing the *same* building artifact the consumer reads.

## Data-acquisition track (complementary)

- **D1 — `ISourceDataProvider`** abstraction; `LocalFolder` first, then `NlsApi` (resolve + download);
  designed so other Nordic providers slot in as siblings.
- **D2 — Tile resolver (local):** location/bbox → covering tiles + halo neighbours via
  `ITileScheme`/`TileNamer`; laser is download-by-tile-name, so resolution needs no API call.
- **D3 — NLS 3D buildings importer** (fast tier): `3d-rakennukset_karttalehti` → **CityGML LOD2** →
  needs a CityGML importer, behind `IBuildingsBuilder`.
- **D4 — LiDAR roof reconstruction** (LOD2): kd-tree + RANSAC/region-growing roof planes; LOD1 fallback.
- *(parked)* kapsi rsync bulk mirror, once we maintain our own tile index.

## Quality / deployment tiers (incl. a zero-backend browser mode)

The pipeline supports a ladder of fidelity, cheapest first:

| Tier | DEM | Buildings | Trees | Runs where |
|---|---|---|---|---|
| **0 — lightweight, zero-backend** | NLS ready **2 m DEM** | NLS **3D CityGML LOD2** | **procedural forests** on cells with no MTK feature | **straight in the browser** — no point clouds, no server |
| **1 — sparse** | DEM from **0.5p** cloud | LiDAR LOD1 / NLS 3D | CHM dominant-tree detection | backend build |
| **2 — dense** | DEM from **5p** cloud | LiDAR **LOD2** roofs | individual-crown detection | backend build |

Tier 0 is a genuinely fun option (your idea): fetch NLS 2 m DEM + 3D buildings (+ MTK rasters for
texturing), scatter trees on "natural / unbuilt" cells, render — **no triangulation, no native LASzip,
no helper server**. Instant-gratification entry point and a strong demo; higher tiers refine on demand
(see progressive DEM below).

## Data freshness & cross-source mismatch

- **Freshness / update detection.** NLS republishes open data periodically. **Put the source's
  version/publish-date in the cache key** (alongside engine + block/output/scheme version), so the app
  can tell "NLS has newer data for this tile" and rebuild only what changed. The versioning you asked
  about should track *source* data, not just engine version.
- **Mismatch is inherent, not a bug.** LiDAR, MTK and 3D buildings have **different capture dates** — a
  building can be in LiDAR but demolished in MTK, or new in MTK with no LiDAR roof yet. No perfect
  reconciliation exists. Sensible policy: **footprint + existence from MTK** (most current vector),
  **height/roof from LiDAR when returns support it, else NLS 3D / default extrusion**; flag strong
  disagreements. Expect it.

## Future: enhanced point-cloud analysis (trees & roofs from LiDAR)

Both already use the cloud (`SimpleTreeCreator` = fixed-window local maxima on the grid;
`BuildingsCreator` = 80th-percentile flat roof). Upgrades, by density:

- **Trees (individual tree detection):** CHM/raster first — **variable-window local maxima** (window
  scales with height), **pit-free CHM** (cheap accuracy win), **watershed** or **Dalponte 2016** for
  crown polygons; point-based (**Li 2012**) for understory on dense data. Sparse 0.5p → dominant trees +
  canopy cover only; dense 5p → individual crowns. `nDSM = DSM − DEM` is ~free from data already computed.
- **Roofs (LOD2):** **RANSAC / region-growing plane segmentation** within each MTK footprint → ridge/eave
  lines; **model-driven primitive fitting** (gable/hip/…) works even at 0.5p because the shape prior
  compensates for sparse points; percentile-flat stays the LOD1 fallback.
- **Data structures:** keep the **2.5D grid** for raster ops (DEM/DSM/CHM — what `VoxelGrid` does well);
  add a **kd-tree** for the 3D neighbour queries these need (PCA normals, RANSAC inliers, point
  segmentation — faster than an octree for k-NN); reserve an **octree** for LOD/streaming (point
  exploration below / COPC). Complement the grid, don't replace it; build indexes **in RAM on demand**.

## Future: progressive DEM refinement & point exploration

An emergent architecture — **DEM as on-demand quality tiers, refined where the user looks**:

1. Show a **3rd-party DEM first** (NLS 2 m) if available → instant coverage.
2. Then **DEM from the sparse 0.5p cloud** (better detail).
3. Then the **most accurate DEM from the dense 5p cloud**.

**User clicks a location → escalate that tile to the next tier.** Needs a per-tile **quality level** in
the job + cache key and a client that swaps the tile artifact in place — fits the per-tile streaming
model already in the design. **Stretch:** click a location to **see the raw LiDAR points** — direct
point-cloud visualization, which is exactly the **octree / COPC streaming** use-case (the one place COPC
earns its keep), separate from the meshed terrain.

## Target framework: netstandard2.0 vs 2.1

Consider **dropping the engine (and `laszipnetstandard`) from `netstandard2.1` to `netstandard2.0`** for
much broader reach (older Unity, .NET Framework consumers) — many `laszipnetstandard` users would
benefit. Cost: 2.0 loses some 2.1 surface (`Span`/`Memory`, some nullable attributes, `IAsyncEnumerable`,
default-interface-members). **Audit current usage first**; if nothing 2.1-specific is load-bearing, it's
a low-cost compatibility win. (Touches the `LangVersion 9` core, the A2 shared-orchestration target, and
the DLL-vendoring story.)

## Open questions / to revisit

- Exact `ITileScheme` surface (neighbours by edge vs. 8-neighbourhood; how sub-tiles/parent are expressed).
- How block size and output size are chosen/configured (per-dataset config? auto from density?).
- Whether block size must be an integer multiple of output size (clean slicing) or arbitrary (bbox slice).
- RAM-budget model: fixed cap vs. measured-available; per-block estimate from point count.
- Output cache naming scheme for the generic grid.
- NLS file-service throughput: job time **~23 s/tile measured**; **download throughput still to
  benchmark** on a good connection (sets cold-start UX / pre-warm).
- MTK ingestion: file-service **SHAPE** (does its output naming match the `TopographicDb` prefixes the
  readers expect?) vs. **Features-API GeoJSON** (read via NTS GeoJSON) — pick one.
- CityGML LOD2 importer: existing .NET lib vs. parse the GML; how to merge with MTK footprints.
- Rate limits / fair-use for automated per-request downloads at product scale.
- Building-source selection policy: when to use NLS 3D (fast) vs. LiDAR roofs (detailed) — by zoom,
  area, or availability.
- Target framework: audit for 2.1-specific APIs, then decide `netstandard2.0` vs `2.1` (broader reach,
  esp. for `laszipnetstandard`).
- Freshness: include the **source-data version/publish-date** in cache keys (not just engine version).
- Tier 0 forest placement: which MTK themes count as "no feature" so procedural forests fill the rest?
- Progressive DEM: per-tile **quality level** in the job/cache key + client-side tile swap on refine.
- Cross-source date-mismatch policy (LiDAR vs MTK vs 3D buildings).
- Other-country providers: which Nordic first, and their CRS/formats/class mappings.
- Probe lives at `D:\tmp\LazProbe` (outside repo) — delete when done.
