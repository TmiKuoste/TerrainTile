---
applyTo: "**"
description: How AI agents and contributors should make changes in this repo.
---

# Repository workflow

- **Branch, don't push to `main`.** Create a branch named `<issue#>-kebab-description`
  (e.g. `22-make-dem-triangulation-faster`) off `main`, matching the existing convention.
  Land changes through a GitHub pull request — merges are PR-based (`Merge pull request #NN`).
- **Commit messages** are short descriptive sentences, typically gerund-led
  ("Adding…", "Fixing…", "Changing…", "Removing…"). Match that style.
- **Stay in the right layer.** A change is Unity-specific only if it belongs under
  `fi.kuoste.terraintile/Runtime/Scripts`. Anything reusable — tile content, builders,
  geometry/raster logic — goes in `TerrainEngine/` and must stay `netstandard2.1`/C# 9.
  Don't pull Unity types or modern .NET APIs into the core to make something compile.
- **Respect the Reader/Creator split.** When adding a content type or builder, provide both
  a cache `Reader` and a source `Creator`, implement the matching `IXxxBuilder`, extend
  `Builder`, check `IsCancellationRequested()` early, and route file paths through the
  interface's `Filename(...)` helper.
- **Mind the DLL boundary.** New core dependencies won't reach Unity until their DLLs are
  vendored into `fi.kuoste.terraintile/Runtime/dll/`. Call this out when you add one.
- **Verify before claiming done.** Build the affected solution (`dotnet build …`). For Unity
  changes, note that play-mode tests run in the Unity Test Runner, not `dotnet test`, and say
  so rather than implying you ran them.
- **Don't commit secrets or build output.** `bin/`, `obj/`, and Azure connection strings
  stay out of the repo; the worker reads credentials from environment variables.
- **Ask before large restructures.** Folder/namespace moves have been deliberate, reviewed
  steps here (see issues #29, #31) — propose the plan before sweeping changes.
