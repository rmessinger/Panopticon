# Task Card `WI-C4f-02`: Preserve NIF rest scale

| Field | Value |
|---|---|
| Card ID | `WI-C4f-02` |
| Roadmap phase | [Track C · C4f](../../track-c-external-assets.md): EE2 scale animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4c-06`, `WI-C4f-01` |
| Diagrams | [C4f architecture diagrams](../../deep-dives/c4f-ee2-scale-animation.md#12-architecture-diagrams) |

## Goal

Relax C4c's non-unit-scale node collapse rule so finite positive NIF node scale becomes C1 bone
rest scale, while singular animated rest scale remains a clear failure.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4f-ee2-scale-animation.md` sections 3, 5, 7, and 9.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` section 5.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifSceneConverter.cs`: C4c node skeleton and scaled
  subtree handling after `WI-C4c-02`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifTransformConverter.cs`: source-to-engine transform
  conversion.
- `src/Panopticon.FileFormats.Nif/Blocks/NifAvObject.cs`: source scale field.
- `test/Panopticon.Simulation.Tests/Content/Importing/NifModelImporterTests.cs`: synthetic hierarchy tests.

## Steps

1. Change the NIF scene conversion rule so `NiNode` and `NiLODNode` scale values greater than
   `1e-4` and finite become `ImportedBone.BindScale`.
2. Keep unit-scale placement identical to C4c.
3. Keep non-positive or non-finite scale collapsed into the nearest ancestor only when the node is
   not targeted by animation.
4. Expose enough target metadata for the animation converter to detect when a KF target resolved to
   a collapsed singular-scale node.
5. Update rest-pose parity tests so scaled rest bones compose to the same converted source world
   matrix as the old bake.
6. Add a synthetic positive non-unit rest scale node and assert `BindScale` is preserved.
7. Add a synthetic zero-rest node under an unanimated subtree and assert static placement remains
   unchanged.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Nif/NifSceneConverter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifTransformConverter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/NifModelImporterTests.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`

## Acceptance criteria

- [ ] Positive non-unit NIF node scale becomes `ImportedBone.BindScale`.
- [ ] Unit-scale C4c proof model descriptors and static captures remain unchanged.
- [ ] Static singular-scale subtrees remain placeable when not animated.
- [ ] The animation importer can report a targeted singular-rest node by name.
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

- Do not convert KF scale keys.
- Do not support rest scale 0 as an animated bone.
- Do not change LOD selection, texture resolution, or material mapping.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4f-01` is done, change `WI-C4f-03` to `Ready`.
- Append a result line to `.exchange`.
