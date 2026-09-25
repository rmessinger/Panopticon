# Task Card `WI-C1-05`: Add model and animation importer contracts

| Field | Value |
|---|---|
| Card ID | `WI-C1-05` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C1-04` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Add `IModelImporter`, `IAnimationImporter`, and `AssetImportContext`, then add the internal
selection path without changing the active W3D loaders yet.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 3 and 6.
- `src/Panopticon.Simulation/Content/OnDemandAssetLoadStrategy.cs`: current loader and resolver ownership.
- `src/Panopticon.Simulation/Content/Loaders/AssetLoadContext.cs`: source for the import-only context
  subset.
- `src/Panopticon.Simulation/IGameDefinition.cs:30-42`: strategy creation contract.
- `src/Panopticon.Mods.Generals/GeneralsDefinition.cs:44-47` and
  `src/Panopticon.Mods.Bfme/BfmeDefinition.cs:44-47`: representative strategy construction.

## Steps

1. Add the two internal interfaces and immutable `AssetImportContext` under
   `Content/Importing/`, matching deep-dive section 3.
2. Add selected importer fields to `OnDemandAssetLoadStrategy`. Keep the current resolver-based
   constructor temporarily and leave existing loader creation behavior unchanged.
3. Add an internal constructor that accepts importer instances plus the existing texture resolver.
   Reject null importers when on-demand loading is enabled.
4. Keep the current public resolver-based constructor for mod-assembly compatibility until
   `WI-C1-12` can replace it with the W3D factory after both importers exist.
5. Keep `OnDemandAssetLoadStrategy.None` unchanged and importer-free.
6. Add tests proving enabled strategies retain supplied fake importers and `None` creates no
   model, hierarchy, or animation loader.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/IModelImporter.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/IAnimationImporter.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/AssetImportContext.cs` (new)
- `src/Panopticon.Simulation/Content/OnDemandAssetLoadStrategy.cs`
- `test/Panopticon.Simulation.Tests/Content/OnDemandAssetLoadStrategyTests.cs` (new)

## Acceptance criteria

- [ ] The importer contracts expose only CPU import values, file system, language, and importer
      identity.
- [ ] Import contracts do not reference graphics or runtime material types.
- [ ] Existing game definitions compile without behavior changes.
- [ ] `None` still supplies no presentation loaders.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~OnDemandAssetLoadStrategyTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not switch any loader to the importer contracts.
- Do not add a global registry; A4 owns experiment selection after C1 lands.
- Do not expose importer contracts publicly or add the W3D factory before both importers exist.
  `WI-C1-12` owns the cross-assembly factory and cutover.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C1-06` and `WI-C1-08` to `Ready`.
- Append a result line to `.exchange`.
