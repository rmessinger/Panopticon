# Task Card `WI-H2b-09`: Convert W3D geometry, skeletons, and animation

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-09` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-03` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Scale W3D vertex positions, bounds, bone pivots, and translation channels to meters at import.

## Context to read (and only this)

- `src/Panopticon.Simulation/Content/Importing/W3d/W3dModelImporter.cs`, `W3dMeshConverter.cs`, `W3dAnimationImporter.cs`.
- Card 01 inventory rows for W3D boxes, HLOD, and draw-module offsets.

## Steps

1. Apply the scale once in the W3D importer per card 02; leave `Panopticon.FileFormats.W3d` source-space.
2. Scale bind translations and animation translation channels together; leave rotations, UVs, normals, and scale keys alone.
3. Test a mesh vertex, bounding box, bone pivot, and animated translation against converted expectations.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/W3d/`.
- Matching importer tests.

## Acceptance criteria

- [ ] A known Generals model's bounds equal legacy bounds times `s`.
- [ ] Bind pose and first animation frame stay aligned (no detached limbs).
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

Plus the targeted tests pinned by `WI-H2b-02`.

## Out of scope / guardrails

No glTF or NIF changes (card 10); no `W3dPatcher` write changes unless card 02 lists them. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-9 in the checklist, and append the result to `.exchange`.
