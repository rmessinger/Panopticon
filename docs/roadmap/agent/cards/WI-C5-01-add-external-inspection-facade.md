# Task Card `WI-C5-01`: Add external inspection facade

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-01` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-10` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Expose engine-owned external asset catalogs and explicit format selection while keeping the C1
model and animation importer interfaces internal.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 2-4 and 12.
- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 2, 3, and 9.
- `src/OpenSage.Game/Content/Importing/IModelImporter.cs`: internal C1 boundary.
- `src/OpenSage.Game/Content/Importing/IAnimationImporter.cs`: internal C1 boundary.
- `src/OpenSage.Game/Content/Importing/AssetImportContext.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedModel.cs`.
- `src/OpenSage.Game/Content/Importing/Gltf/GltfImportOptions.cs` (new, from `WI-C4-01`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfModelImporter.cs` (new, from `WI-C4-10`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfAnimationImporter.cs` (new, from `WI-C4-08`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfImportDiagnostics.cs` (new, from `WI-C4-10`).

## Steps

1. Add immutable public `ExternalAssetCatalog`, `ExternalAnimationDescriptor`, and inspection
   diagnostic values containing only the counts, bounds, feature identifiers, names, durations,
   channel counts, stable animation keys, and parser warnings listed in deep-dive section 4.
2. Add public `IExternalAssetInspector` with one format-neutral model inspection operation over a
   virtual asset key and `AssetImportContext`.
3. Add immutable `ExternalFormatSelection` containing a public format identifier, immutable
   options, and inspector facade. Do not place either C1 importer interface in its public shape.
4. Add `ExternalAssetInspection` extension selection for `.gltf` and `.glb`. Return a public
   format-selection result carrying `EXT001 UnsupportedFormat` for unknown or compound extensions.
5. Implement the glTF inspector under `Content/Importing/Gltf` by reusing the C4 document loader,
   resolver, limits, subset checks, and diagnostics. Do not create a second parser path.
6. Add an internal engine factory that maps the public format identifier and options to the C4
   model and animation importers for the TestHarness friend assembly.
7. Add focused tests for extension selection, catalog immutability, stable animation keys, public
   API isolation, and equality between inspector and importer limit behavior.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/ExternalAssetCatalog.cs` (new)
- `src/OpenSage.Game/Content/Importing/ExternalAnimationDescriptor.cs` (new)
- `src/OpenSage.Game/Content/Importing/ExternalAssetInspectionDiagnostic.cs` (new)
- `src/OpenSage.Game/Content/Importing/IExternalAssetInspector.cs` (new)
- `src/OpenSage.Game/Content/Importing/ExternalFormatSelection.cs` (new)
- `src/OpenSage.Game/Content/Importing/ExternalFormatSelectionResult.cs` (new)
- `src/OpenSage.Game/Content/Importing/ExternalAssetInspection.cs` (new)
- `src/OpenSage.Game/Content/Importing/ExternalFormatFactory.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfExternalAssetInspector.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/ExternalAssetInspectionTests.cs` (new)

## Acceptance criteria

- [ ] Public catalogs contain no SharpGLTF object, parser object, mutable collection, or runtime
      graphics asset.
- [ ] Public format selection exposes neither `IModelImporter` nor `IAnimationImporter`.
- [ ] `.gltf` and `.glb` select one explicit glTF inspector and unknown formats return an
      `EXT001` selection result.
- [ ] Inspector and importer use the same resolver, limits, subset policy, and animation key form.
- [ ] The internal factory has one explicit glTF entry and no process-global registry.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExternalAssetInspectionTests|FullyQualifiedName~GltfImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not make either C1 importer interface public or add friend access for ReplaySketch Core.
- Do not add a global registry or change shipped-game asset load strategies.
- Do not materialize models, bind animations, or allocate graphics resources.
- Do not implement C6 morph targets or advanced material extensions.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C5-03` to `Ready`.
- If done and `WI-C4-02` is done, change `WI-C5-02` to `Ready`.
- Append a result line to `.exchange`.
