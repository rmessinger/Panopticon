# Task Card `WI-C3-09`: Wire PBR model materialization

| Field | Value |
|---|---|
| Card ID | `WI-C3-09` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C3-02`, `WI-C3-03`, `WI-C3-08` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Route imported PBR meshes through validation, optional tangent generation, texture
materialization, and the core PBR shader without changing existing W3D material paths.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` sections 4-6.
- C3 validator, tangent generator, texture materializer, and shader resources.
- `src/OpenSage.Game/Content/Loaders/ImportedModelMaterializer.cs`.
- `src/OpenSage.Game/Graphics/Shaders/ShaderResourceManager.cs`.
- `src/OpenSage.Game/Graphics/ModelMesh.cs`.
- `src/OpenSage.Game/Graphics/ModelMesh.W3d.cs`.

## Steps

1. Register the PBR shader resources in `ShaderResourceManager`.
2. Validate the complete imported graph before materialization allocates any resource.
3. Generate tangents only for normal-mapped PBR mesh parts that need them.
4. Materialize PBR image and sampler bindings and request the cached PBR material.
5. Preserve the existing mesh vertex/index buffer, skinning, bounds, and mesh-part construction.
6. Keep fixed-function and named shader-family dispatch unchanged.

## Files likely to change

- `src/OpenSage.Game/Graphics/Shaders/ShaderResourceManager.cs`
- `src/OpenSage.Game/Content/Loaders/ImportedModelMaterializer.cs`
- `src/OpenSage.Game/Graphics/ModelMesh.cs`

## Acceptance criteria

- [ ] PBR materials reach the dedicated PBR shader path.
- [ ] Validation precedes tangent generation, file access, and GPU allocation.
- [ ] Static and skinned PBR meshes use the existing runtime vertex and mesh types.
- [ ] Existing W3D material dispatch and missing-texture behavior remain unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~ImportedModelMaterializerTests|FullyQualifiedName~W3dImportParityTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add a source-format branch to runtime graphics types.
- Do not apply tangent generation to W3D meshes or imported tangents.
- Do not change mesh skinning limits or vertex layout.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C3-10` to `Ready`.
- Append a result line to `.exchange`.
