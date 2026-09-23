# Task Card `WI-C4c-02`: Preserve the NIF node hierarchy as bones

| Field | Value |
|---|---|
| Card ID | `WI-C4c-02` |
| Roadmap phase | [Track C · C4c](../../track-c-external-assets.md): EE2 rigid KF animation |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4a-07`, `WI-C4b-02` |
| Diagrams | [C4c architecture diagrams](../../deep-dives/c4c-ee2-rigid-animation.md#10-architecture-diagrams) |

## Goal

Make `NifModelImporter` emit one `ImportedBone` per unit-scale `NiNode`, with rigid sub-objects in
bone-local space, while keeping the rest pose identical to the C4a bake.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` section 5.
- `src/OpenSage.Game/Content/Importing/Nif/NifSceneConverter.cs` and `NifModelImporter.cs`.
- `src/OpenSage.Game/Content/Importing/Nif/NifTransformConverter.cs:19-116`.
- `src/OpenSage.Game/Content/Importing/ImportedBone.cs`, `ImportedSkeleton.cs`, and
  `ImportedSubObject.cs`.
- `src/OpenSage.Game.Tests/Content/Importing/NifModelImporterTests.cs` and
  `Ee2NifImportTests.cs:71-110`.

## Steps

1. In `NifSceneConverter`, walk depth-first with the root first. Each `NiNode` or `NiLODNode`
   whose scale is within 1e-4 of 1 becomes a bone named after its source node, with the nearest
   bone ancestor as parent.
2. Convert each bone local transform by conjugation with the source-to-engine matrix. Add a
   `NifTransformConverter` helper that returns engine translation and rotation. Prove it with a
   unit test that composing converted locals equals the converted source world.
3. A non-unit-scale node and its descendants bake into the nearest bone ancestor frame, using the
   C4a bake math relative to that bone.
4. Bake each `NiTriShape` local transform into positions and normals in its parent bone's engine
   frame. Keep winding, UV, material, and LOD handling unchanged. Attach it with
   `parentBoneIndex` set to that bone, and compute bounds in bone-local space.
5. Expose one internal entry point that returns the skeleton and bone rest transforms for a NIF
   file. `WI-C4c-04` reuses it.
6. Update synthetic importer tests for hierarchy, parent indices, and rest-pose world positions.
   Add a scaled-node collapse case.
7. Replace the single-bone assertions in `Ee2NifImportTests`. Expect 30 Galley bones and 22
   Bomber bones, and keep the locked world AABBs and all other descriptor checks.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Nif/NifSceneConverter.cs`
- `src/OpenSage.Game/Content/Importing/Nif/NifModelImporter.cs`
- `src/OpenSage.Game/Content/Importing/Nif/NifTransformConverter.cs`
- `src/OpenSage.Game.Tests/Content/Importing/NifModelImporterTests.cs`
- `src/OpenSage.Game.Tests/Content/Importing/Ee2NifImportTests.cs`

## Acceptance criteria

- [ ] Every unit-scale node is a bone with its source name, and parent indices precede children.
- [ ] Rest-pose world positions equal the previous C4a bake within 1e-4, and the locked world
      AABBs still pass.
- [ ] A non-unit-scale subtree collapses into its nearest bone without placement change.
- [ ] Re-running `Ee2NifProof` produces static captures visually identical to the committed
      C4b guide expectations.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifModelImporterTests"
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2NifImportTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add animation import, scale to `ImportedBone`, or skinning.
- Do not change LOD selection, material mapping, or texcache behavior.
- If rest-pose parity cannot hold, stop and report instead of loosening tolerances.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4c-03` is done, change `WI-C4c-04` to `Ready`.
- Append a result line to `.exchange`.
