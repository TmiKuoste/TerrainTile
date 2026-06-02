---
applyTo: "fi.kuoste.terraintile/**"
description: Rules for the Unity package (fi.kuoste.terraintile).
---

# Unity package (fi.kuoste.terraintile)

- **Unity is one output profile**, not the core. Only code that genuinely needs `UnityEngine`
  belongs here under `Runtime/Scripts`; reusable logic goes in `TerrainEngine/` instead.
- **Engine DLLs are vendored** in `Runtime/dll/`. When the core engine gains a dependency,
  the corresponding DLL must be added here too, or the package won't resolve it at runtime.
- **Respect the asmdef boundaries** (`Tiles.asmdef`, `Tools.asmdef`, `PlayModeTests.asmdef`).
  Don't introduce references that create cycles between runtime assemblies.
- **Logging** flows through `ILogger`; the Unity side provides `UnityLogger`. Don't call
  `Debug.Log` directly from code meant to be shared.
- **Tests are play-mode tests** under `Tests/PlayModeTests`, run through the Unity Test Runner
  — **not** `dotnet test`. If you change tested behavior, say the tests must be run in Unity
  rather than implying you ran them.
- **`.meta` files matter.** Don't delete or rename assets without their `.meta`; let Unity
  manage GUIDs.
