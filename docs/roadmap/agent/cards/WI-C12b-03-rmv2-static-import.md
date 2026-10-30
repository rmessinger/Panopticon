# Task Card `WI-C12b-03`: `Rmv2File` reader and static `RMV2` import

| Field | Value |
|---|---|
| Card ID | `WI-C12b-03` |
| Roadmap phase | [Track C · C12b](../../track-c-external-assets.md): SHOGUN 2 static models and parts |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C12b-01` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

An `RMV2` importer (id `RMV2`) imports versions 0 to 2 static models as `ImportedModel` with geometry, normals, UVs, and placeholder materials; other versions and signatures fail with the pack path.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 3, 9, 11 (class diagram and static import sequence), and the results of `WI-C12b-01`.
- `src/Panopticon.Simulation/Content/Importing/IModelImporter.cs`, `ImportedModel.cs`, `ImportedMesh.cs`, `ImportedVertex.cs`, `ExternalFormatFactory.cs`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifModelImporter.cs` (importer precedent).

## Steps

1. Add `Rmv2File.Read(stream)` to `src/Panopticon.FileFormats.Shogun2` for versions 0 to 2 with the layouts recorded by `WI-C12b-01`; unknown versions, vertex formats, and the `.rigid_model` signature throw with offset.
2. Add `Rmv2ModelImporter : IModelImporter` in `src/Panopticon.Simulation/Content/Importing/Shogun2/`, importing LOD 0 meshes into `ImportedMesh` with the engine axis convention, and failing meshes over 65,535 vertices with file and mesh name.
3. Register `RMV2` in `ExternalFormatFactory` and expose the texture-directory string on each mesh for `WI-C12b-04`.
4. Add synthetic tests per version (one triangle mesh each) and rejection tests.
5. Add an Interactive test that imports every RMV2 file in `models.pack` and reports counts by version and failure reason, with the castle gate piece and Armstrong gun importing.

## Files likely to change

- `src/Panopticon.FileFormats.Shogun2/Rmv2File.cs` and supporting types (new)
- `src/Panopticon.Simulation/Content/Importing/Shogun2/Rmv2ModelImporter.cs` (new), `ExternalFormatFactory.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Shogun2/` (new tests)

## Acceptance criteria

- [ ] Synthetic versions 0, 1, and 2 import with expected positions, normals, and UVs.
- [ ] Interactive: the two proof models import; the corpus failure count is recorded in deep dive section 3.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Shogun2Rmv2"
```

## Out of scope / guardrails

- Materials and textures (`WI-C12b-04`).
- `VMPF` parts (`WI-C12b-04`).
- LODs above 0.
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C12b-04` to `Ready` if `WI-C12b-02` is `Done`.
- Append a result line to `.exchange`.
