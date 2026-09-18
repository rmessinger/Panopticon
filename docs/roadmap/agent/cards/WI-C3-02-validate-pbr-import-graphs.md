# Task Card `WI-C3-02`: Validate PBR import graphs

| Field | Value |
|---|---|
| Card ID | `WI-C3-02` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C3-01` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Validate the complete imported PBR material and mesh graph before any GPU or asset-store access.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` section 4.
- PBR interchange types from `WI-C3-01`.
- `src/OpenSage.Game/Content/Importing/ImportedModel.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedMesh.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedMeshPart.cs`.
- `src/OpenSage.Game/Content/Loaders/ImportedModelMaterializer.cs`.
- `src/OpenSage.Game.Tests/Content/Importing/ImportedModelMaterializerTests.cs`.

## Steps

1. Extract the C1 model-materializer validation into one CPU-only
   `ImportedModelValidator` when needed, then extend that same pass for PBR. Do not create a
   second validation path.
2. Validate finite factor ranges, alpha cutoff, image sources, sampler values, texture roles,
   material references, and UV selection.
3. Validate that each selected UV set is complete for the mesh part and that
   `ImportedMeshPart.BlendEnabled` agrees with the PBR alpha mode.
4. Require normals and either complete tangents or tangent-generation inputs for normal mapping.
5. Throw contextual `InvalidDataException` for malformed data and `NotSupportedException` for
   valid unsupported combinations.
6. Add a guard test proving malformed input fails before the first GPU or file-system access.

## Files likely to change

- `src/OpenSage.Game/Content/Loaders/ImportedModelValidator.cs` (new)
- `src/OpenSage.Game/Content/Loaders/ImportedModelMaterializer.cs`
- `src/OpenSage.Game.Tests/Content/Importing/ImportedModelMaterializerTests.cs`

## Acceptance criteria

- [ ] Every deep-dive section 4 invariant has a focused failing test.
- [ ] Error messages identify the model, sub-object, mesh part, and material where applicable.
- [ ] Validation completes before GPU allocation and external-image access.
- [ ] Existing non-PBR material validation remains unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedModelMaterializerTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not silently clamp factors or substitute broken image references.
- Do not generate tangents in the validator.
- Do not change W3D missing-texture fallback behavior.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C3-03` and `WI-C3-08` are done, change `WI-C3-09` to `Ready`.
- Append a result line to `.exchange`.
