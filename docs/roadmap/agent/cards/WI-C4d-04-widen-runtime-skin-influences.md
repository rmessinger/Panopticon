# Task Card `WI-C4d-04`: Widen runtime skinning to four influences

| Field | Value |
|---|---|
| Card ID | `WI-C4d-04` |
| Roadmap phase | [Track C · C4d](../../track-c-external-assets.md): EE2 skinned character animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4d-03` |
| Diagrams | [C4d architecture diagrams](../../deep-dives/c4d-ee2-skinned-animation.md#12-architecture-diagrams) |

## Goal

Carry four bone indices and weights through the shared mesh vertex layout and every mesh vertex
shader, with W3D rendering unchanged.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md` section 8.
- `src/OpenSage.Game/Graphics/Shaders/MeshShaderResources.cs:118-150`.
- `src/OpenSage.Game/Graphics/ModelMesh.Imported.cs:50-72`.
- `src/OpenSage.Game/Assets/Shaders/Mesh.h:5-15` and `61-77`.
- `src/OpenSage.Game/Assets/Shaders/FixedFunction.vert`, `NormalMapped.vert`, `Simple.vert`,
  `MeshDepth.vert`, and `PbrMetallicRoughness.vert`.
- `src/OpenSage.Game.Tests/Content/Importing/W3dImportParityTests.cs:490-500`.

## Steps

1. Append `BoneIndex2`, `BoneIndex3` (`uint`) and `BoneWeight2`, `BoneWeight3` (`float`) to
   `MeshVertex.Basic` after `BoneWeight1`, and add matching `UInt1` and `Float1` elements to the
   first `VertexLayoutDescription` in the same order.
2. In `Mesh.h`, declare the new inputs at locations 10 to 13 and move `in_UV0` and `in_UV1` to 14
   and 15.
3. Extend `GetSkinnedVertexData` with `skinning2`, `skinning3`, `weight2`, and `weight3`. Add
   the two terms using `inputPosition0` and `inputNormal0`.
4. Pass `_SkinningBuffer[in_BoneIndex2]`, `_SkinningBuffer[in_BoneIndex3]`, `in_BoneWeight2`,
   and `in_BoneWeight3` from all five vertex shaders. Extend the PBR tangent and binormal blends
   to four terms.
5. In `ModelMesh.Imported.CreateVertices`, copy the two new slots from `ImportedVertex`.
6. Add a GPU-free test that `Marshal.SizeOf<MeshVertex.Basic>()` is 104 and that the layout
   element count matches the struct fields. Update the W3D parity size calculation if it
   hard-codes a size.
7. Run a W3D model capture before and after and confirm identical output.

## Files likely to change

- `src/OpenSage.Game/Graphics/Shaders/MeshShaderResources.cs`
- `src/OpenSage.Game/Graphics/ModelMesh.Imported.cs`
- `src/OpenSage.Game/Assets/Shaders/Mesh.h`
- `src/OpenSage.Game/Assets/Shaders/FixedFunction.vert`
- `src/OpenSage.Game/Assets/Shaders/NormalMapped.vert`
- `src/OpenSage.Game/Assets/Shaders/Simple.vert`
- `src/OpenSage.Game/Assets/Shaders/MeshDepth.vert`
- `src/OpenSage.Game/Assets/Shaders/PbrMetallicRoughness.vert`
- `src/OpenSage.Game.Tests/Graphics/MeshVertexLayoutTests.cs` (new)

## Acceptance criteria

- [ ] Shaders compile in the normal build.
- [ ] `MeshVertex.Basic` is 104 bytes and matches its layout description.
- [ ] A W3D model capture is identical before and after.
- [ ] Existing glTF skinned fixtures render unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MeshVertexLayoutTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add a second vertex layout or pipeline family.
- Do not change the skinning-buffer upload or `ModelInstance.ComputeSkinningMatrix`.
- Do not change importers.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C4d-05` to `Ready`.
- If done and `WI-C4d-06` is done, change `WI-C4d-07` to `Ready`.
- Append a result line to `.exchange`.
