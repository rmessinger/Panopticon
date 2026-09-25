# Task Card `WI-C4-01`: Bootstrap glTF import

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-01` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C1-14` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Add the isolated SharpGLTF dependency and the immutable configuration, key parser, and bounded
root-document scaffold required by both glTF importers.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 1-3 and 9.
- `src/Directory.Packages.props`: centrally managed package versions.
- `src/Panopticon.Simulation/Panopticon.Simulation.csproj`: engine package references.
- `src/Panopticon.Simulation/Content/Importing/IModelImporter.cs`.
- `src/Panopticon.Simulation/Content/Importing/IAnimationImporter.cs`.
- `src/Panopticon.Simulation/Content/Importing/AssetImportContext.cs`.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dModelImporter.cs`: null and exception conventions.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs`: animation-key precedent.

## Steps

1. Add centrally managed `SharpGLTF.Core` version `1.0.7` and reference it only from
   `Panopticon.Simulation`.
2. Add public sealed immutable `GltfImportOptions` with a positive finite `UnitsPerMeter`
   defaulting to `1 / 0.3048`, plus positive root-document and data-URI byte limits used by the
   initial loader.
3. Add internal `GltfAssetKey` parsing for normalized `.gltf` and `.glb` model keys and exact
   `<model-key>#animation=<non-negative-index>` animation keys. Reject fragments on model keys and
   all other query or fragment forms by returning no parsed key.
4. Add internal `GltfDocumentLoader` that obtains the root through
   `AssetImportContext.FileSystem.GetFile`, checks `FileSystemEntry.Length` before allocation,
   copies bounded root bytes, and invokes SharpGLTF strict parsing through an injectable resource
   callback seam.
5. Return null for an invalid key or absent root. Translate malformed JSON or GLB parse failures
   to `InvalidDataException` with the normalized source path.
6. Add focused tests for option validation, key normalization and animation indices, missing
   roots, oversized roots, and malformed root documents.

## Files likely to change

- `src/Directory.Packages.props`
- `src/Panopticon.Simulation/Panopticon.Simulation.csproj`
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfImportOptions.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfAssetKey.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfDocumentLoader.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/GltfImporterScaffoldTests.cs` (new)

## Acceptance criteria

- [ ] Package version management remains central and only `SharpGLTF.Core` is referenced.
- [ ] `GltfImportOptions` exposes no mutable collection or setter.
- [ ] Model and animation keys accept only the documented forms.
- [ ] Root bytes are bounded before allocation and opened only through the supplied file system.
- [ ] No SharpGLTF type appears in a public signature.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfImporterScaffoldTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not resolve external resources, convert scenes, or construct a complete importer.
- Do not add SharpGLTF Runtime or Toolkit packages.
- Do not extend `AssetImportContext` or add a global importer registry.
- Do not use `System.IO.File`, `Directory`, or the process current directory.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4-02` to `Ready`.
- Append a result line to `.exchange`.
