# Task Card `WI-C1-13`: Switch loaders and shipped games to importers

| Field | Value |
|---|---|
| Card ID | `WI-C1-13` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C1-12` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Switch model, hierarchy, and animation loaders to import then materialize, and make every shipped
game select the internal W3D importers through the public strategy factory.

## Context to read (and only this)

- Passing `W3dImportParityTests` from `WI-C1-12`.
- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 3, 4, and 9.
- `src/OpenSage.Game/Content/OnDemandAssetLoadStrategy.cs`.
- `src/OpenSage.Game/Content/AssetStore.cs:137-140,184-202,311-314`.
- Model, hierarchy, and animation loaders under `src/OpenSage.Game/Content/Loaders/`.
- All `CreateAssetLoadStrategy` implementations under `src/OpenSage.Mods.*`.

## Steps

1. Change all three presentation-asset loaders to delegate to the selected importer, then
   materialize the result.
2. Pass importers from `OnDemandAssetLoadStrategy` into those loaders.
3. Add public `OnDemandAssetLoadStrategy.CreateW3d(IPathResolver, IPathResolver)` inside
   `OpenSage.Game`, then replace each mod assembly's resolver-based constructor call with that
   factory while preserving its current model and texture resolvers.
4. Remove the temporary public resolver-based constructor after repository search shows no caller.
5. Add fake-importer tests proving one import per cache miss, runtime caching, hierarchy
   delegation, and null behavior for top-level missing assets.
6. Re-run data-only and W3D parity tests after cutover.

## Files likely to change

- `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs`
- `src/OpenSage.Game/Content/Loaders/OnDemandModelBoneHierarchyLoader.cs`
- `src/OpenSage.Game/Content/Loaders/OnDemandAnimationLoader.cs`
- `src/OpenSage.Game/Content/OnDemandAssetLoadStrategy.cs`
- `src/OpenSage.Mods.*/` game definition files
- `src/OpenSage.Game.Tests/Content/OnDemandImporterWiringTests.cs` (new)

## Acceptance criteria

- [ ] Model, hierarchy, and animation loaders consume only importer contracts.
- [ ] Every shipped game selects W3D importers through the public in-assembly factory.
- [ ] Cache misses invoke the selected importer once and cache the runtime asset.
- [ ] Top-level misses return null; broken referenced sources throw contextual exceptions.
- [ ] Data-only loading and W3D parity remain unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~W3dImportParityTests"
dotnet build src
dotnet test src --filter "FullyQualifiedName~OnDemandImporterWiringTests|FullyQualifiedName~DataOnlyAssetLoadingTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not delete direct W3D runtime constructors; `WI-C1-14` removes them after cutover.
- Do not make importer contracts public or add mod assemblies to `InternalsVisibleTo`.
- Do not add a registry, glTF support, retargeting, or PBR translation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C1-14` to `Ready`.
- Append a result line to `.exchange`.
