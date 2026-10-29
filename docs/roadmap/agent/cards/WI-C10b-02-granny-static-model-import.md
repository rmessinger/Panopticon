# Task Card `WI-C10b-02`: Granny static model and skeleton import

| Field | Value |
|---|---|
| Card ID | `WI-C10b-02` |
| Roadmap phase | [Track C · C10b](../../track-c-external-assets.md): AoE3 DE static Granny models |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C10b-01` |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

`GrannyModelImporter` (`IModelImporter`, id `GR2`) imports static meshes and the bind-pose skeleton, normalized to engine axes and units.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` sections 3, 7, the class diagram, and the static sequence diagram.
- `src/Panopticon.Simulation/Content/Importing/IModelImporter.cs`, `ImportedModel.cs`, `ImportedMesh.cs`, `ImportedVertex.cs`, `ImportedSkeleton.cs`, `ImportedBone.cs`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifModelImporter.cs` (importer shape, key handling, null for missing files).

## Steps

1. Add `GrannyModelImporter` under `src/Panopticon.Simulation/Content/Importing/Granny/`: resolve the key as a path in `AssetImportContext.FileSystem`, return null when missing, and wrap reader errors with the key.
2. Add `GrannyAxisSystem.FromArtToolInfo`: units per meter and right, up, back vectors to an engine-space basis change applied to positions, normals, tangents, and bind transforms.
3. Skeleton: bones with `ParentIndex`, local `Transform` (position, orientation, scale-shear) as bind TRS, and `InverseWorldTransform` as `InverseBindMatrix`; `ImportSkeleton` returns it alone.
4. Add `GrannyMeshConverter`: vertex layout by component name (`Position`, `Normal`, `Tangent`, `Binormal`, `TextureCoordinates0`), triangle topology groups to mesh parts with one material slot each; split or reject per the 16-bit index rule in section 7.
5. Rigid meshes only here: a mesh with `BoneBindings` of more than one bone imports bound to its first bone and logs that skinning is `WI-C10c-01`.
6. Materials: a placeholder PBR material per group with the Granny material name recorded as a property, replaced in `WI-C10b-03`.
7. Interactive tests: musketeer and hussar horse import with recorded bone, mesh, vertex, and triangle counts and bounds.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Granny/` (new: `GrannyModelImporter.cs`, `GrannyMeshConverter.cs`, `GrannySkeletonConverter.cs`, `GrannyAxisSystem.cs`)
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/GrannyModelImportTests.cs` (new)

## Acceptance criteria

- [ ] Musketeer and hussar horse import with counts and engine-space bounds recorded in the deep dive (upright, plausible meters).
- [ ] Every bone's inverse bind times its bind world matrix is identity within 1e-4.
- [ ] Missing keys return null; compressed files fail with key, section, and compression type.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
AOE3DE_PATH="/mnt/pit2/Network Storage/Transfer/AoE3DE" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Aoe3"
```

## Out of scope / guardrails

- Skin weights (`WI-C10c-01`).
- `.material.XMB` mapping (`WI-C10b-03`).
- LOD files (`lp_*`, `*_low`).
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10c-01` to `Ready`; set `WI-C10b-03` to `Ready` if `WI-C10a-02` and `WI-C10a-03` are done.
- Tick C10b in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
