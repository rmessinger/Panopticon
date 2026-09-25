# Task Card `WI-C3-01`: Add PBR interchange contracts

| Field | Value |
|---|---|
| Card ID | `WI-C3-01` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C1-14` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Represent the core metallic-roughness material model, texture bindings, image sources, samplers,
and alpha policy as immutable format-neutral interchange values.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` sections 1-3.
- `src/Panopticon.Simulation/Content/Importing/ImportedMaterial.cs`.
- `src/Panopticon.Simulation/Content/Importing/ImportedMaterialProperty.cs`.
- `src/Panopticon.Simulation/Content/Importing/ImportedMeshPart.cs`.
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedAssetTests.cs`.

## Steps

1. Add engine-semantic enums for texture role, address/filter modes, and PBR alpha mode.
2. Add immutable `ImportedImageSource` cases for normalized virtual paths and copied encoded bytes
   with MIME type and diagnostic name.
3. Add immutable `ImportedSampler` and `ImportedTextureBinding` values.
4. Add `ImportedPbrMetallicRoughness` with the factors, optional bindings, alpha cutoff, and
   double-sided state defined by the deep dive.
5. Add `ImportedMaterial.PbrMetallicRoughnessFamily` and require that shader family and the PBR
   payload appear together.
6. Make the PBR payload authoritative for culling, blending, depth-write, and alpha-test behavior.
   Provide a PBR construction path that derives the legacy render-state fields rather than asking
   callers to duplicate them.
7. Reject a material that combines PBR with fixed-function shading.
8. Add constructor-default, copied-byte, and invalid-discriminant tests.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/ImportedImageSource.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedSampler.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedTextureBinding.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedPbrMetallicRoughness.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedMaterial.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedAssetTests.cs`

## Acceptance criteria

- [ ] The complete core PBR payload is expressible without glTF or GPU types.
- [ ] External paths and embedded bytes retain exact immutable identity and diagnostics.
- [ ] Fixed-function and named shader-family materials remain constructible unchanged.
- [ ] PBR family, payload, and derived legacy render-state fields cannot contradict one another.
- [ ] Fixed-function and PBR payloads cannot coexist.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedAssetTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not use glTF names or numeric constants in the interchange contract.
- Do not add texture transforms, vertex colors, morph targets, or `KHR_materials_*` fields.
- Do not decode images or allocate GPU resources.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C3-02`, `WI-C3-03`, `WI-C3-04`, and `WI-C3-06` to `Ready`.
- Append a result line to `.exchange`.
