---
applyTo: "TerrainEngine/**"
description: Rules for the platform-independent .NET core (Common + TileBuilders).
---

# TerrainEngine core (.NET)

- **Target `netstandard2.1` / C# 9.** This core is consumed by Unity — do not use newer
  language or runtime features here. `LangVersion` is pinned to 9.0 in the csproj.
- **`Nullable` is enabled** — honor the annotations; don't suppress with `!` to silence them.
- **Namespaces follow the folder path** under `Kuoste.TerrainEngine.*`.
- **Reader/Creator contract.** Each content type exposes an `IXxxBuilder` interface in
  `Common/Interfaces/` with two implementations under `TileBuilders/`:
  - `XxxReader` — deserializes already-built content from the intermediate cache
    (e.g. `DemDsmReader` calls `VoxelGrid.Deserialize`).
  - `XxxCreator` — produces the content from source data when no cache exists.
  When adding a content type, provide **both**, implement the interface, and extend the
  shared `Builder` base (`TileBuilders/Builder.cs`).
- **Check `IsCancellationRequested()` early** in every `Build()` method — builders run
  concurrently and must bail out cheaply when cancelled.
- **Use the interface's `static Filename(name, version)` helper** for cache file names rather
  than hardcoding paths.
- **Keep it Unity-agnostic.** No `UnityEngine` references — Unity integration lives in the
  `fi.kuoste.terraintile` package and injects its logger via `ILogger`.
- **New dependencies ripple to Unity.** Any package you add here must have its DLL vendored
  into `fi.kuoste.terraintile/Runtime/dll/` or the Unity package won't resolve it — call this
  out in your change.
