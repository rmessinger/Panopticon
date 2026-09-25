# Task Card `WI-C4-10`: Enforce glTF import policy

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-10` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C4-05`, `WI-C4-06`, `WI-C4-07`, `WI-C4-08`, `WI-C4-09` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Compose the model and animation importers, enforce the complete C4 extension, limit, and exception
contract, and keep construction explicit for internal callers.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 1, 2, 8, and 9.
- `src/Panopticon.Simulation/Content/Importing/IModelImporter.cs`.
- `src/Panopticon.Simulation/Content/Importing/IAnimationImporter.cs`.
- `src/Panopticon.Simulation/Content/Importing/AssetImportContext.cs`.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dModelImporter.cs`: importer failure boundary.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs`: animation failure boundary.
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfImportOptions.cs` (new from `WI-C4-01`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfDocumentLoader.cs` (new from `WI-C4-01`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfResourceResolver.cs` (new from `WI-C4-02`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfScenePlan.cs` (new from `WI-C4-05`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfSceneConverter.cs` (new from `WI-C4-05`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfAccessorDecoder.cs` (new from `WI-C4-06`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfPrimitiveConverter.cs` (new from `WI-C4-06`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfSkinConverter.cs` (new from `WI-C4-07`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfAnimationConverter.cs` (new from `WI-C4-08`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfAnimationImporter.cs` (new from `WI-C4-08`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfMaterialConverter.cs` (new from `WI-C4-09`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfImageSourceConverter.cs` (new from `WI-C4-09`).
- `test/Panopticon.Simulation.Tests/Content/Importing/GltfImporterScaffoldTests.cs` (new from `WI-C4-01`).
- `test/Panopticon.Simulation.Tests/Content/Importing/GltfResourceResolverTests.cs` (new from `WI-C4-02`).
- `test/Panopticon.Simulation.Tests/Content/Importing/GltfSceneConverterTests.cs` (new from `WI-C4-05`).
- `test/Panopticon.Simulation.Tests/Content/Importing/GltfPrimitiveConverterTests.cs` (new from `WI-C4-06`).
- `test/Panopticon.Simulation.Tests/Content/Importing/GltfSkinConverterTests.cs` (new from `WI-C4-07`).
- `test/Panopticon.Simulation.Tests/Content/Importing/GltfAnimationImporterTests.cs` (new from `WI-C4-08`).
- `test/Panopticon.Simulation.Tests/Content/Importing/GltfMaterialConverterTests.cs` (new from `WI-C4-09`).

## Steps

1. Add `GltfModelImporter` implementing `IModelImporter` and complete
   `GltfAnimationImporter` implementing `IAnimationImporter`. Give each an internal constructor
   that takes immutable `GltfImportOptions` and explicit internal collaborators or diagnostics.
2. Compose document loading, scene planning, primitive conversion, skinning, animation, and PBR
   conversion into complete imported graphs. Never return a partial graph after a conversion
   failure.
3. Extend `GltfImportOptions` with positive immutable limits for aggregate external bytes; node,
   mesh, primitive, vertex, index, joint, animation, channel, and keyframe counts; embedded image
   bytes and dimensions; and nesting depth.
4. Check limits before allocation or iteration that scales with the declared count. Treat every
   limit violation as contextual `InvalidDataException`.
5. Inspect `extensionsRequired` before conversion. Reject every unsupported required extension.
   Ignore an optional unsupported extension only when complete core semantics remain, and emit
   one diagnostic per extension name per import.
6. Normalize parser, reference, accessor, and numeric failures to `InvalidDataException`.
   Normalize valid excluded features to `NotSupportedException`. Include source path and the most
   specific available scene, node, mesh, primitive, material, animation, or accessor index.
7. Keep constructors internal and instantiate them directly in tests and later C5/TestHarness
   callers. Do not add a global registry, service locator, shipped-game registration, or
   extension-based runtime branch.
8. Add whole-import tests for null roots, no partial output, each limit family, required and
   optional extensions, exception type and context, explicit construction, and package-boundary
   isolation.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfImportOptions.cs` (new from `WI-C4-01`)
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfModelImporter.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfAnimationImporter.cs` (new from `WI-C4-08`)
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfImportDiagnostics.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfDocumentLoader.cs` (new from `WI-C4-01`)
- `test/Panopticon.Simulation.Tests/Content/Importing/GltfImporterTests.cs` (new)
- C4 focused test files only when adding whole-import limit assertions

## Acceptance criteria

- [ ] Model and animation importers return null only for invalid keys or missing root documents.
- [ ] Malformed data, limit violations, and excluded valid features use the documented exception
      categories with asset context.
- [ ] Unsupported required extensions never disappear silently; eligible optional extensions
      produce one warning each.
- [ ] Every configured limit is enforced before the corresponding unbounded allocation.
- [ ] Importers are explicitly constructible by internal callers with no registry.
- [ ] `SharpGLTF` references exist only under `Content/Importing/Gltf` and project files.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfImporterTests|FullyQualifiedName~GltfAnimationImporterTests|FullyQualifiedName~GltfMaterialConverterTests"
if git grep -n --untracked "SharpGLTF" -- 'src/Panopticon.Simulation/**/*.cs' | grep -v "Content/Importing/Gltf/"; then exit 1; fi
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

The `git grep` pipeline must produce no output.

## Out of scope / guardrails

- Do not register glTF with `OnDemandAssetLoadStrategy`, an `IGameDefinition`, or a process-global
  importer collection.
- Do not cache parser objects or retain an asset file system after an import completes.
- Do not downgrade malformed data to null or return a partially converted model.
- Do not add deferred C6 features.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4-11` to `Ready`.
- If done, change `WI-C5-01` to `Ready`.
- Append a result line to `.exchange`.
